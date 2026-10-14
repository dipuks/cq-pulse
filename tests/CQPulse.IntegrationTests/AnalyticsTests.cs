using CQPulse.Application.Analytics;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.IntegrationTests.Infrastructure;

namespace CQPulse.IntegrationTests;

/// <summary>
/// Analytics aggregation matrix. Each test uses its own project and filters by it, so data from other tests in the
/// shared database cannot affect the expected numbers. Rows are inserted with exact UTC timestamps.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalyticsTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset Day1 = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_dataset_returns_zeroes_and_empty_series()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={project.Id}");
        var trends = await client.GetJsonAsync<List<TrendPointDto>>($"/api/analytics/execution-trends?projectId={project.Id}");
        var failures = await client.GetJsonAsync<List<FailureCategoryCountDto>>($"/api/analytics/failure-breakdown?projectId={project.Id}");

        Assert.Equal(new AnalyticsSummaryDto(0, 0, 0, 0, 0, 0, null), summary);
        Assert.Empty(trends);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Totals_rates_and_average_span_multiple_runs()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await InsertRunAsync(project.Id, [(Day1.AddHours(1), TestOutcome.Passed, 10, null), (Day1.AddHours(2), TestOutcome.Failed, 20, FailureCategory.Timeout)]);
        await InsertRunAsync(project.Id, [(Day1.AddHours(3), TestOutcome.Skipped, 30, null)]);

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={project.Id}");

        // 1 of 3 passed: 33.33… rounds to 33.3; average duration (10+20+30)/3.
        Assert.Equal(new AnalyticsSummaryDto(3, 1, 1, 1, 33.3, 33.3, 20), summary);
    }

    [Fact]
    public async Task Date_filter_is_a_half_open_utc_interval()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await InsertRunAsync(project.Id,
        [
            (Day1.AddTicks(-1), TestOutcome.Passed, 1, null),                        // previous day: excluded
            (Day1, TestOutcome.Passed, 1, null),                                      // from boundary: included
            (Day1.AddDays(1).AddTicks(-10), TestOutcome.Failed, 1, FailureCategory.ApiError), // last moment of day 1
            (Day1.AddDays(1), TestOutcome.Failed, 1, FailureCategory.ApiError)        // to boundary: excluded
        ]);
        var range = $"projectId={project.Id}&fromUtc={Uri.EscapeDataString(Day1.ToString("O"))}&toUtc={Uri.EscapeDataString(Day1.AddDays(1).ToString("O"))}";

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?{range}");
        var trends = await client.GetJsonAsync<List<TrendPointDto>>($"/api/analytics/execution-trends?{range}");
        var allTrends = await client.GetJsonAsync<List<TrendPointDto>>($"/api/analytics/execution-trends?projectId={project.Id}");

        Assert.Equal((2, 1, 1), (summary.Total, summary.Passed, summary.Failed));
        Assert.Equal([new TrendPointDto(new DateOnly(2030, 1, 1), 1, 1, 0, 1)], trends);
        Assert.Equal([new DateOnly(2029, 12, 31), new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 2)], allTrends.Select(t => t.Date));
    }

    [Fact]
    public async Task Failure_breakdown_counts_categories_and_ignores_uncategorized_failures()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await InsertRunAsync(project.Id,
        [
            (Day1, TestOutcome.Failed, 1, FailureCategory.Timeout),
            (Day1, TestOutcome.Failed, 1, FailureCategory.Timeout),
            (Day1, TestOutcome.Failed, 1, FailureCategory.ApiError),
            (Day1, TestOutcome.Failed, 1, null),            // failed without category: counted as failed, not in breakdown
            (Day1, TestOutcome.Skipped, 1, null)
        ], suiteId: null);

        var failures = await client.GetJsonAsync<List<FailureCategoryCountDto>>($"/api/analytics/failure-breakdown?projectId={project.Id}");
        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={project.Id}");

        Assert.Equal([new FailureCategoryCountDto(FailureCategory.Timeout, 2), new FailureCategoryCountDto(FailureCategory.ApiError, 1)], failures);
        Assert.Equal((5, 4, 1, 80.0), (summary.Total, summary.Failed, summary.Skipped, summary.FailureRate));
    }

    [Fact]
    public async Task Project_filter_excludes_other_projects()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var first = await client.CreateProjectAsync();
        var second = await client.CreateProjectAsync();
        await InsertRunAsync(first.Id, [(Day1, TestOutcome.Passed, 1, null)]);
        await InsertRunAsync(second.Id, [(Day1, TestOutcome.Failed, 1, FailureCategory.Environment), (Day1, TestOutcome.Failed, 1, FailureCategory.Environment)]);

        var firstSummary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={first.Id}");
        var secondSummary = await client.GetJsonAsync<AnalyticsSummaryDto>($"/api/analytics/summary?projectId={second.Id}");

        Assert.Equal((1, 100.0), (firstSummary.Total, firstSummary.PassRate));
        Assert.Equal((2, 0.0, 100.0), (secondSummary.Total, secondSummary.PassRate, secondSummary.FailureRate));
    }

    /// <summary>Inserts a terminal run (no suite, no seed) and its results directly.</summary>
    private Task InsertRunAsync(Guid projectId, (DateTimeOffset CompletedAt, TestOutcome Outcome, int DurationMs, FailureCategory? Category)[] results, Guid? suiteId = null) =>
        fixture.WithDbContextAsync(async db =>
        {
            var run = new TestRun
            {
                ProjectId = projectId, TestSuiteId = suiteId, RunLabel = "Analytics fixture", ConfigurationSnapshot = "{}", TotalTests = results.Length,
                CompletedTests = results.Length,
                PassedCount = results.Count(x => x.Outcome == TestOutcome.Passed),
                FailedCount = results.Count(x => x.Outcome == TestOutcome.Failed),
                SkippedCount = results.Count(x => x.Outcome == TestOutcome.Skipped)
            };
            run.Start(Day1.AddDays(-2));
            run.Complete(Day1.AddDays(2));
            db.TestRuns.Add(run);
            db.TestCaseResults.AddRange(results.Select((r, i) => new TestCaseResult
            {
                TestRunId = run.Id, TestCaseNameSnapshot = $"Fixture {i}", Outcome = r.Outcome, DurationMs = r.DurationMs,
                StartedAtUtc = r.CompletedAt.AddMilliseconds(-r.DurationMs), CompletedAtUtc = r.CompletedAt, FailureCategory = r.Category
            }));
            await db.SaveChangesAsync();
            return 0;
        });
}
