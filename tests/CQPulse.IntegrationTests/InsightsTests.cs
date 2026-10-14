using System.Net;
using CQPulse.Application.Catalog;
using CQPulse.Application.Insights;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.IntegrationTests.Infrastructure;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>Historical insights over persisted results, using per-test projects and rows at controlled UTC dates.</summary>
[Collection(ApiCollection.Name)]
public sealed class InsightsTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset TodayNoon = new(DateTimeOffset.UtcNow.UtcDateTime.Date.AddHours(12), TimeSpan.Zero);

    [Fact]
    public async Task Failure_signatures_group_variants_and_compare_with_the_previous_window()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await InsertAsync(project.Id, null,
        [
            (TodayNoon, TestOutcome.Failed, FailureCategory.Timeout, "Timeout after 3000 ms for order 42"),
            (TodayNoon.AddDays(-1), TestOutcome.Failed, FailureCategory.Timeout, "Timeout after 5000 ms for order 7"),
            (TodayNoon.AddDays(-2), TestOutcome.Failed, FailureCategory.Timeout, "Timeout after 5000 ms for order 9"),
            (TodayNoon, TestOutcome.Failed, FailureCategory.UiElement, "Element #submit not found"),
            (TodayNoon.AddDays(-10), TestOutcome.Failed, FailureCategory.Timeout, "Timeout after 1 ms for order 1"), // previous 7-day window
            (TodayNoon, TestOutcome.Passed, null, null)
        ]);

        var report = await client.GetJsonAsync<FailureSignatureReportDto>($"/api/insights/failure-signatures?projectId={project.Id}&days=7");

        Assert.Equal(4, report.TotalFailures);
        var timeout = report.Signatures[0];
        Assert.Equal((FailureCategory.Timeout, "Timeout after <n> ms for order <n>", 3, 1, 3, true), // three raw variants
            (timeout.Category, timeout.NormalizedMessage, timeout.Occurrences, timeout.PreviousOccurrences, timeout.MessageVariants, timeout.CountsAreUpperBounds));
        var ui = report.Signatures[1];
        Assert.Equal((1, 0, 1, false), (ui.Occurrences, ui.PreviousOccurrences, ui.MessageVariants, ui.CountsAreUpperBounds));
        Assert.Equal(report.Window.FromUtc.AddDays(-7), report.Window.PreviousFromUtc);
    }

    [Fact]
    public async Task Flaky_candidates_need_mixed_outcomes_and_enough_executions()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var suite = await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Flaky suite", null))).ReadAsync<TestSuiteDto>();
        TestCaseDto Case(string name, SimulatedOutcome outcome) => client.PostJsonAsync($"/api/suites/{suite.Id}/cases",
            new SaveTestCaseRequest(name, null, true, TestPriority.Medium, outcome, 1)).Result.ReadAsync<TestCaseDto>().Result;
        var flaky = Case("Checkout sometimes fails", SimulatedOutcome.Random);
        var stable = Case("Always passes", SimulatedOutcome.Passed);
        var rare = Case("Ran twice", SimulatedOutcome.Random);

        TestOutcome[] sequence = [TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Skipped];
        await InsertAsync(project.Id, suite.Id,
            sequence.Select((o, i) => (TodayNoon.AddHours(-i * 3), o, o == TestOutcome.Failed ? FailureCategory.ApiError : (FailureCategory?)null, (string?)"e", (Guid?)flaky.Id))
                .Concat(Enumerable.Range(0, 6).Select(i => (TodayNoon.AddHours(-i), TestOutcome.Passed, (FailureCategory?)null, (string?)null, (Guid?)stable.Id)))
                .Concat([(TodayNoon, TestOutcome.Passed, null, null, rare.Id), (TodayNoon.AddHours(-1), TestOutcome.Failed, FailureCategory.Timeout, "t", rare.Id)])
                .ToArray());

        var report = await client.GetJsonAsync<FlakyReportDto>($"/api/insights/flaky-tests?projectId={project.Id}&days=7&minExecutions=5");

        var candidate = Assert.Single(report.Candidates);
        Assert.Equal((flaky.Id, 6, 3, 2, 1, true), (candidate.TestCaseId, candidate.Executions, candidate.Passed, candidate.Failed, candidate.Skipped, candidate.RandomnessIsConfigured));
        Assert.Equal(0.3333, candidate.FailureRate);
        Assert.Equal(3, candidate.OutcomeChanges); // chronological F,P,P,F,P (skip ignored) → 3 switches
        Assert.Equal(3, report.CasesEvaluated);
    }

    [Fact]
    public async Task Anomalies_are_flagged_against_a_baseline_with_explicit_parameters()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var rows = new List<(DateTimeOffset, TestOutcome, FailureCategory?, string?)>();
        for (var day = 1; day <= 8; day++)
            for (var i = 0; i < 20; i++)
                rows.Add((TodayNoon.AddDays(-day), i < (day % 2 == 0 ? 2 : 3) ? TestOutcome.Failed : TestOutcome.Passed, null, null)); // 10–15%
        for (var i = 0; i < 20; i++) rows.Add((TodayNoon, i < 12 ? TestOutcome.Failed : TestOutcome.Passed, null, null)); // 60% today
        await InsertAsync(project.Id, null, rows.ToArray());

        var report = await client.GetJsonAsync<AnomalyReportDto>($"/api/insights/anomalies?projectId={project.Id}&days=7&baselineDays=7&minimumBaselineDays=5&minimumDailyResults=20&threshold=3");

        var today = report.Days.Last();
        Assert.Equal((20, 12, 0.6, true, "Failure rate unusually high"), (today.Results, today.Failed, today.FailureRate, today.IsAnomaly, today.Assessment));
        Assert.Equal(7, today.BaselineDays);
        Assert.Equal(7, report.Days.Count);
        Assert.DoesNotContain(report.Days.SkipLast(1), d => d.IsAnomaly);
        Assert.Contains("z-score", report.Method);
        Assert.Equal((7, 5, 20, 3.0), (report.BaselineDays, report.MinimumBaselineDays, report.MinimumDailyResults, report.Threshold));
    }

    [Fact]
    public async Task Quality_trends_compare_windows_and_decline_to_predict()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        await InsertAsync(project.Id, null,
        [
            (TodayNoon, TestOutcome.Failed, FailureCategory.Timeout, "x"), (TodayNoon, TestOutcome.Passed, null, null),           // 50% now
            (TodayNoon.AddDays(-8), TestOutcome.Failed, FailureCategory.Timeout, "x"),
            (TodayNoon.AddDays(-8), TestOutcome.Passed, null, null), (TodayNoon.AddDays(-8), TestOutcome.Passed, null, null),
            (TodayNoon.AddDays(-8), TestOutcome.Passed, null, null)                                                            // 25% before
        ]);

        var report = await client.GetJsonAsync<QualityTrendReportDto>("/api/insights/quality-trends?days=7");

        var trend = report.Projects.Single(x => x.ProjectId == project.Id);
        Assert.Equal((2, 1, 0.5, 4, 0.25, 25.0), (trend.Results, trend.Failed, trend.FailureRate, trend.PreviousResults, trend.PreviousFailureRate, trend.ChangePercentagePoints));
        Assert.StartsWith("Not offered", report.PredictionStatus);
    }

    [Fact]
    public async Task Invalid_anomaly_threshold_is_rejected_and_endpoints_require_admin()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        using var viewer = await fixture.CreateClientAsync(fixture.NonAdministrator);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/insights/anomalies?threshold=0.5")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/insights/failure-signatures")).StatusCode);
    }

    [Fact]
    public async Task Catalog_runs_with_random_cases_surface_configured_flakiness_end_to_end()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var suite = await (await client.PostJsonAsync($"/api/projects/{project.Id}/suites", new SaveTestSuiteRequest("Random suite", null))).ReadAsync<TestSuiteDto>();
        await (await client.PostJsonAsync($"/api/suites/{suite.Id}/cases", new SaveTestCaseRequest("Coin flip", null, true, TestPriority.Low, SimulatedOutcome.Random, 0))).ReadAsync<TestCaseDto>();
        var request = NewRequest(project.Id, 10) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog, PassPercentage = 50, FailurePercentage = 50, SkippedPercentage = 0 };
        await client.RunToCompletionAsync(request);

        var report = await client.GetJsonAsync<FlakyReportDto>($"/api/insights/flaky-tests?projectId={project.Id}&days=1&minExecutions=5");

        var candidate = Assert.Single(report.Candidates);
        Assert.Equal((10, 5, 5, true), (candidate.Executions, candidate.Passed, candidate.Failed, candidate.RandomnessIsConfigured));
    }

    private Task InsertAsync(Guid projectId, Guid? suiteId, (DateTimeOffset At, TestOutcome Outcome, FailureCategory? Category, string? Message)[] rows) =>
        InsertAsync(projectId, suiteId, rows.Select(r => (r.At, r.Outcome, r.Category, r.Message, (Guid?)null)).ToArray());

    private Task InsertAsync(Guid projectId, Guid? suiteId, (DateTimeOffset At, TestOutcome Outcome, FailureCategory? Category, string? Message, Guid? CaseId)[] rows) =>
        fixture.WithDbContextAsync(async db =>
        {
            var run = new TestRun
            {
                ProjectId = projectId, TestSuiteId = suiteId, RunLabel = "Insights fixture", ConfigurationSnapshot = "{}", TotalTests = rows.Length,
                CompletedTests = rows.Length, PassedCount = rows.Count(x => x.Outcome == TestOutcome.Passed),
                FailedCount = rows.Count(x => x.Outcome == TestOutcome.Failed), SkippedCount = rows.Count(x => x.Outcome == TestOutcome.Skipped)
            };
            run.Start(TodayNoon.AddDays(-30));
            run.Complete(TodayNoon);
            db.TestRuns.Add(run);
            db.TestCaseResults.AddRange(rows.Select((r, i) => new TestCaseResult
            {
                TestRunId = run.Id, TestCaseId = r.CaseId, TestCaseNameSnapshot = $"Insight {i}", Outcome = r.Outcome, DurationMs = 1,
                StartedAtUtc = r.At, CompletedAtUtc = r.At, FailureCategory = r.Category, ErrorMessage = r.Message
            }));
            await db.SaveChangesAsync();
            return 0;
        });
}
