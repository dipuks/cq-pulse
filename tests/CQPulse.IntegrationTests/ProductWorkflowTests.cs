using System.Net;
using System.Net.Http.Json;
using CQPulse.Application.Catalog;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>M7: catalog-driven simulation, test-case lifecycle and history, rerun, project summary, filters.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProductWorkflowTests(ApiFixture fixture)
{
    [Fact]
    public async Task Catalog_run_executes_real_cases_with_exact_preview_totals()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var (project, suite, cases) = await CreateCatalogAsync(client);
        var request = NewRequest(project.Id, 40) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog };

        var preview = await (await client.PostJsonAsync("/api/simulator/preview", request)).ReadAsync<SimulationPreview>();
        var run = await client.RunToCompletionAsync(request);

        Assert.Equal(WorkloadSource.Catalog, preview.WorkloadSource);
        Assert.Equal(4, preview.CatalogCaseCount); // the disabled case is excluded
        Assert.Equal((preview.ExpectedPassed, preview.ExpectedFailed, preview.ExpectedSkipped), (run.PassedCount, run.FailedCount, run.SkippedCount));
        var perCase = await fixture.WithDbContextAsync(db => db.TestCaseResults.Where(x => x.TestRunId == run.Id)
            .GroupBy(x => new { x.TestCaseId, x.TestCaseNameSnapshot })
            .Select(g => new { g.Key.TestCaseId, g.Key.TestCaseNameSnapshot, Count = g.Count(), Failed = g.Count(x => x.Outcome == TestOutcome.Failed), MaxDuration = g.Max(x => x.DurationMs) })
            .ToListAsync());
        Assert.Equal(4, perCase.Count);
        Assert.All(perCase, c => Assert.Equal(10, c.Count));
        Assert.DoesNotContain(perCase, c => c.TestCaseId == cases["Disabled"].Id);
        Assert.Equal(10, perCase.Single(c => c.TestCaseId == cases["Always fails"].Id).Failed);
        Assert.Equal(0, perCase.Single(c => c.TestCaseId == cases["Always passes"].Id).Failed);
        Assert.Equal("Always fails", perCase.Single(c => c.TestCaseId == cases["Always fails"].Id).TestCaseNameSnapshot);
        Assert.Equal(15, perCase.Single(c => c.TestCaseId == cases["Always passes"].Id).MaxDuration);
    }

    [Fact]
    public async Task Catalog_run_without_enabled_cases_is_rejected()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var suite = await CreateSuiteAsync(client, project.Id, "Empty");

        var response = await client.PostJsonAsync("/api/simulator/runs",
            NewRequest(project.Id, 10) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no enabled test cases", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Editing_and_deleting_a_case_preserves_its_execution_history()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var (project, suite, cases) = await CreateCatalogAsync(client);
        var run = await client.RunToCompletionAsync(NewRequest(project.Id, 8) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog });
        var target = cases["Always fails"];

        var history = await client.GetJsonAsync<PageResponse<TestCaseExecutionDto>>($"/api/test-cases/{target.Id}/results");
        Assert.Equal(2, history.TotalCount);
        Assert.All(history.Items, h => Assert.Equal((run.Id, run.RunLabel, TestOutcome.Failed), (h.RunId, h.RunLabel, h.Outcome)));

        var renamed = await client.PutAsJsonAsync($"/api/test-cases/{target.Id}",
            new SaveTestCaseRequest("Renamed case", null, true, TestPriority.High, SimulatedOutcome.Passed, 1), Json);
        renamed.EnsureSuccessStatusCode();
        var afterRename = await client.GetJsonAsync<PageResponse<TestCaseExecutionDto>>($"/api/test-cases/{target.Id}/results");
        Assert.All(afterRename.Items, h => Assert.Equal(("Always fails", TestOutcome.Failed), (h.TestCaseNameSnapshot, h.Outcome)));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/test-cases/{target.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/test-cases/{target.Id}/results")).StatusCode);
        var preserved = await fixture.WithDbContextAsync(db => db.TestCaseResults
            .Where(x => x.TestRunId == run.Id && x.TestCaseNameSnapshot == "Always fails").Select(x => x.TestCaseId).ToListAsync());
        Assert.Equal(2, preserved.Count);
        Assert.All(preserved, id => Assert.Null(id));
        Assert.Equal((run.PassedCount, run.FailedCount), ((await client.GetRunAsync(run.Id)).PassedCount, (await client.GetRunAsync(run.Id)).FailedCount));
    }

    [Fact]
    public async Task Disabling_a_case_excludes_it_from_future_runs_only()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var (project, suite, cases) = await CreateCatalogAsync(client);
        var request = NewRequest(project.Id, 4) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog };
        var first = await client.RunToCompletionAsync(request);

        var patched = await client.PatchAsJsonAsync($"/api/test-cases/{cases["Always passes"].Id}/enabled", new SetEnabledRequest(false), Json);
        var disabled = await patched.ReadAsync<TestCaseDto>();
        var second = await client.RunToCompletionAsync(request);

        Assert.False(disabled.Enabled);
        var executedIds = await fixture.WithDbContextAsync(db => db.TestCaseResults.Where(x => x.TestRunId == second.Id)
            .Select(x => x.TestCaseId).Distinct().ToListAsync());
        Assert.DoesNotContain(cases["Always passes"].Id, executedIds.Cast<Guid>());
        Assert.Equal(1, (await client.GetJsonAsync<PageResponse<TestCaseExecutionDto>>($"/api/test-cases/{cases["Always passes"].Id}/results")).TotalCount);
        Assert.Equal(first.TotalTests, (await client.GetRunAsync(first.Id)).CompletedTests);
    }

    [Fact]
    public async Task Rerun_creates_a_linked_run_with_the_same_configuration_and_outcomes()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var original = await client.RunToCompletionAsync(NewRequest(project.Id, 150, seed: 77));

        var response = await client.PostAsync($"/api/runs/{original.Id}/rerun", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rerunId = (await response.ReadAsync<QueuedRun>()).RunId;
        var rerun = await client.WaitForTerminalAsync(rerunId);
        var originalAfter = await client.GetRunAsync(original.Id);

        Assert.Equal(original.Id, rerun.RerunOfRunId);
        Assert.Equal([rerunId], originalAfter.RerunIds);
        Assert.Equal($"Rerun of {original.RunLabel}", rerun.RunLabel);
        Assert.Equal(original.RandomSeed, rerun.RandomSeed);
        Assert.Equal((original.PassedCount, original.FailedCount, original.SkippedCount), (rerun.PassedCount, rerun.FailedCount, rerun.SkippedCount));
        var listed = await client.GetJsonAsync<PageResponse<RunSummaryDto>>($"/api/runs?projectId={project.Id}");
        Assert.Contains(listed.Items, r => r.Id == rerunId && r.RerunOfRunId == original.Id);
    }

    [Fact]
    public async Task Rerun_errors_are_reported()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var slow = await client.QueueRunAsync(NewRequest(project.Id, 300) with { MaxConcurrency = 1, MinimumDurationMs = 10, MaximumDurationMs = 10 });

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/runs/{Guid.NewGuid()}/rerun", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/runs/{slow}/rerun", null)).StatusCode); // target busy

        await client.PostAsync($"/api/simulator/runs/{slow}/cancel", null);
        await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 1));
    }

    [Fact]
    public async Task Project_summary_reports_persisted_counts()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var (project, suite, _) = await CreateCatalogAsync(client);
        await CreateSuiteAsync(client, project.Id, "Second suite");
        var runs = new List<RunDetailDto>();
        for (var i = 0; i < 6; i++)
            runs.Add(await client.RunToCompletionAsync(NewRequest(project.Id, 20) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog }));

        var summary = await client.GetJsonAsync<ProjectSummaryDto>($"/api/projects/{project.Id}/summary");

        Assert.Equal((2, 5, 4, 6, 0), (summary.SuiteCount, summary.TestCaseCount, summary.EnabledTestCaseCount, summary.RunCount, summary.ActiveRunCount));
        Assert.Equal(runs.Sum(r => r.PassedCount), summary.PassedResults);
        Assert.Equal(runs.Sum(r => r.FailedCount), summary.FailedResults);
        Assert.Equal(120, summary.PassedResults + summary.FailedResults + summary.SkippedResults);
        Assert.Equal(5, summary.RecentRuns.Count);
        Assert.Equal(runs[^1].Id, summary.RecentRuns[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/projects/{Guid.NewGuid()}/summary")).StatusCode);
    }

    [Fact]
    public async Task List_filters_narrow_projects_cases_and_runs()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var active = await client.CreateProjectAsync($"Filter {marker} active");
        await (await client.PostJsonAsync("/api/projects", new SaveProjectRequest($"Filter {marker} archived", null, ProjectStatus.Archived))).ReadAsync<ProjectDto>();
        var (_, suite, cases) = await CreateCatalogAsync(client, active);
        var other = await CreateSuiteAsync(client, active.Id, "Other suite");
        await (await client.PostJsonAsync($"/api/suites/{other.Id}/cases", new SaveTestCaseRequest("Other case", null, true, TestPriority.Low, SimulatedOutcome.Passed, 1))).ReadAsync<TestCaseDto>();
        var suiteRun = await client.RunToCompletionAsync(NewRequest(active.Id, 4) with { TestSuiteId = suite.Id, WorkloadSource = WorkloadSource.Catalog });
        await client.RunToCompletionAsync(NewRequest(active.Id, 4) with { TestSuiteId = other.Id, WorkloadSource = WorkloadSource.Catalog });

        var archived = await client.GetJsonAsync<PageResponse<ProjectDto>>($"/api/projects?search={marker}&status=Archived");
        var newestFirst = await client.GetJsonAsync<PageResponse<ProjectDto>>($"/api/projects?search={marker}&sort=created-desc");
        var disabledCases = await client.GetJsonAsync<PageResponse<TestCaseDto>>($"/api/suites/{suite.Id}/cases?enabled=false");
        var criticalCases = await client.GetJsonAsync<PageResponse<TestCaseDto>>($"/api/suites/{suite.Id}/cases?priority=Critical");
        var suiteRuns = await client.GetJsonAsync<PageResponse<RunSummaryDto>>($"/api/runs?testSuiteId={suite.Id}");

        Assert.Equal([$"Filter {marker} archived"], archived.Items.Select(x => x.Name));
        Assert.Equal($"Filter {marker} archived", newestFirst.Items[0].Name);
        Assert.Equal([cases["Disabled"].Id], disabledCases.Items.Select(x => x.Id));
        Assert.Equal([cases["Always fails"].Id], criticalCases.Items.Select(x => x.Id));
        Assert.Equal([suiteRun.Id], suiteRuns.Items.Select(x => x.Id));
    }

    private static async Task<(ProjectDto Project, TestSuiteDto Suite, Dictionary<string, TestCaseDto> Cases)> CreateCatalogAsync(
        HttpClient client, ProjectDto? project = null)
    {
        project ??= await client.CreateProjectAsync();
        var suite = await CreateSuiteAsync(client, project.Id, "Checkout");
        var cases = new Dictionary<string, TestCaseDto>();
        foreach (var (name, outcome, duration, priority, enabled) in new[]
                 {
                     ("Always passes", SimulatedOutcome.Passed, 15, TestPriority.Medium, true),
                     ("Always fails", SimulatedOutcome.Failed, 5, TestPriority.Critical, true),
                     ("Random A", SimulatedOutcome.Random, 1, TestPriority.Low, true),
                     ("Random B", SimulatedOutcome.Random, 2, TestPriority.Low, true),
                     ("Disabled", SimulatedOutcome.Passed, 1, TestPriority.Low, false)
                 })
        {
            cases[name] = await (await client.PostJsonAsync($"/api/suites/{suite.Id}/cases",
                new SaveTestCaseRequest(name, null, enabled, priority, outcome, duration))).ReadAsync<TestCaseDto>();
        }
        return (project, suite, cases);
    }

    private static async Task<TestSuiteDto> CreateSuiteAsync(HttpClient client, Guid projectId, string name) =>
        await (await client.PostJsonAsync($"/api/projects/{projectId}/suites", new SaveTestSuiteRequest(name, null))).ReadAsync<TestSuiteDto>();

    private sealed record QueuedRun(Guid RunId);
}
