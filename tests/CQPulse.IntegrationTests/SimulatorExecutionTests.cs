using System.Net;
using CQPulse.Application.Catalog;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Simulator;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SimulatorExecutionTests(ApiFixture fixture)
{
    [Fact]
    public async Task Completed_run_matches_preview_exactly_and_counters_equal_persisted_rows()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var request = NewRequest(project.Id, testCount: 1_237);

        var preview = await (await client.PostJsonAsync("/api/simulator/preview", request)).ReadAsync<SimulationPreview>();
        var run = await client.RunToCompletionAsync(request);

        Assert.Equal(RunStatus.CompletedWithFailures, run.Status);
        Assert.Equal(project.Name, preview.ProjectName);
        Assert.Equal(1_237, preview.ExpectedPassed + preview.ExpectedFailed + preview.ExpectedSkipped);
        Assert.Equal((preview.ExpectedPassed, preview.ExpectedFailed, preview.ExpectedSkipped),
            (run.PassedCount, run.FailedCount, run.SkippedCount));
        Assert.Equal(1_237, run.CompletedTests);

        var persisted = await CountPersistedAsync(run.Id);
        Assert.Equal((run.PassedCount, run.FailedCount, run.SkippedCount), persisted.ByOutcome);
        Assert.Equal(1_237, persisted.DistinctNames); // no duplicate persistence
        Assert.Equal(run.FailedCount, persisted.CategorizedFailures);
    }

    [Fact]
    public async Task Seeded_runs_are_reproducible()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var first = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 300, seed: 2026));
        var second = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 300, seed: 2026));

        Assert.Equal(await OutcomeFingerprintAsync(first.Id), await OutcomeFingerprintAsync(second.Id));
    }

    [Fact]
    public async Task Running_run_exposes_live_progress_through_rest()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        // ~2,000 tests at 5 ms with concurrency 4: several seconds, with many 500-result batches persisted mid-run.
        var runId = await client.QueueRunAsync(NewRequest(project.Id, 2_000) with { MaxConcurrency = 4, MinimumDurationMs = 5, MaximumDurationMs = 5 });

        RunDetailDto? midRun = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (midRun is null && DateTime.UtcNow < deadline)
        {
            var current = await client.GetRunAsync(runId);
            if (current.Status == RunStatus.Running && current.CompletedTests > 0) midRun = current;
            else await Task.Delay(50);
        }

        await client.WaitForTerminalAsync(runId, TimeSpan.FromSeconds(60));
        Assert.NotNull(midRun);
        Assert.InRange(midRun.CompletedTests, 1, 1_999);
        Assert.Equal(midRun.CompletedTests, midRun.PassedCount + midRun.FailedCount + midRun.SkippedCount);
    }

    [Fact]
    public async Task Cancelled_run_stays_cancelled_with_counters_equal_to_persisted_rows()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var runId = await client.QueueRunAsync(NewRequest(project.Id, 5_000) with { MaxConcurrency = 4, MinimumDurationMs = 5, MaximumDurationMs = 5 });
        await WaitUntilAsync(async () => (await client.GetRunAsync(runId)).CompletedTests > 0);

        var cancel = await client.PostAsync($"/api/simulator/runs/{runId}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        // The worker processes runs sequentially, so a follow-up run completing proves the cancelled run was finalized.
        await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 5));
        var run = await client.GetRunAsync(runId);
        var persisted = await CountPersistedAsync(runId);

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Equal("Cancelled by user.", run.CancellationReason);
        Assert.InRange(run.CompletedTests, 1, 4_999);
        Assert.Equal((run.PassedCount, run.FailedCount, run.SkippedCount), persisted.ByOutcome);
    }

    [Fact]
    public async Task Persistence_failure_marks_run_failed_without_deadlocking()
    {
        // A tiny buffer and batch size make producers block on a full channel if the consumer stops draining.
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Simulator:ResultBufferCapacity", "4");
            builder.UseSetting("Simulator:ResultBatchSize", "10");
            builder.ConfigureTestServices(services => services.AddSingleton<IResultBatchWriter>(provider =>
                new FailingAfterFirstBatchWriter(new EfResultBatchWriter(provider.GetRequiredService<IServiceScopeFactory>()))));
        });
        using var client = await CreateAdministratorClientAsync(factory);
        var project = await client.CreateProjectAsync();

        var run = await client.WaitForTerminalAsync(await client.QueueRunAsync(NewRequest(project.Id, 500)), TimeSpan.FromSeconds(20));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("Result persistence failed", run.ErrorSummary);
        Assert.Equal(10, run.CompletedTests);
        Assert.Equal((run.PassedCount, run.FailedCount, run.SkippedCount), (await CountPersistedAsync(run.Id)).ByOutcome);
    }

    [Fact]
    public async Task Progress_notification_failures_do_not_fail_the_run()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IRunProgressPublisher, ThrowingProgressPublisher>()));
        using var client = await CreateAdministratorClientAsync(factory);
        var project = await client.CreateProjectAsync();

        var run = await client.RunToCompletionAsync(NewRequest(project.Id, 150));

        Assert.Equal(RunStatus.CompletedWithFailures, run.Status);
        Assert.Equal(150, run.CompletedTests);
    }

    [Fact]
    public async Task Startup_recovery_fails_interrupted_runs_and_leaves_terminal_runs_untouched()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var otherProject = await client.CreateProjectAsync();
        var completed = await client.RunToCompletionAsync(NewRequest(project.Id, 25));
        var (queuedId, runningId) = await fixture.WithDbContextAsync(async db =>
        {
            var queued = NewRun(project.Id, "Interrupted while queued");
            // Separate projects: at most one queued/running run per target is allowed (UX_TestRuns_ActiveTarget).
            var running = NewRun(otherProject.Id, "Interrupted while running");
            running.Start(DateTimeOffset.UtcNow.AddMinutes(-1));
            db.TestRuns.AddRange(queued, running);
            await db.SaveChangesAsync();
            return (queued.Id, running.Id);
        });

        // A new host instance runs ExecutionRecoveryService at startup, as after a crash or restart.
        await using (var restarted = fixture.Factory.WithWebHostBuilder(_ => { }))
        {
            restarted.CreateClient().Dispose();
        }

        var queuedAfter = await client.GetRunAsync(queuedId);
        var runningAfter = await client.GetRunAsync(runningId);
        var completedAfter = await client.GetRunAsync(completed.Id);
        Assert.Equal(RunStatus.Failed, queuedAfter.Status);
        Assert.Equal(RunStatus.Failed, runningAfter.Status);
        Assert.Contains("interrupted", runningAfter.ErrorSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(runningAfter.CompletedAtUtc);
        Assert.Equal(completed, completedAfter);
    }

    [Fact]
    public async Task Limits_endpoint_reports_configured_limits()
    {
        using var client = await fixture.CreateAdministratorClientAsync();

        var limits = await client.GetJsonAsync<SimulatorLimitsDto>("/api/simulator/limits");

        Assert.Equal(new SimulatorLimitsDto(50_000, 128, 60_000, 10_000), limits);
    }

    [Fact]
    public async Task Second_active_run_for_same_target_is_rejected_with_409()
    {
        using var client = await fixture.CreateAdministratorClientAsync();
        var project = await client.CreateProjectAsync();
        var slow = NewRequest(project.Id, 400) with { MaxConcurrency = 2, MinimumDurationMs = 10, MaximumDurationMs = 10 };
        var runId = await client.QueueRunAsync(slow);

        var duplicate = await client.PostJsonAsync("/api/simulator/runs", slow);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await client.PostAsync($"/api/simulator/runs/{runId}/cancel", null);
        await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 5));
    }

    [Fact]
    public async Task Readiness_check_reports_database_healthy()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> CreateAdministratorClientAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", await fixture.GetTokenAsync(fixture.Administrator));
        return client;
    }

    private Task<PersistedCounts> CountPersistedAsync(Guid runId) => fixture.WithDbContextAsync(async db =>
    {
        var results = db.TestCaseResults.Where(x => x.TestRunId == runId);
        return new PersistedCounts(
            (await results.CountAsync(x => x.Outcome == TestOutcome.Passed),
             await results.CountAsync(x => x.Outcome == TestOutcome.Failed),
             await results.CountAsync(x => x.Outcome == TestOutcome.Skipped)),
            await results.Select(x => x.TestCaseNameSnapshot).Distinct().CountAsync(),
            await results.CountAsync(x => x.Outcome == TestOutcome.Failed && x.FailureCategory != null));
    });

    private Task<string> OutcomeFingerprintAsync(Guid runId) => fixture.WithDbContextAsync(async db =>
        string.Join(";", (await db.TestCaseResults.Where(x => x.TestRunId == runId)
                .Select(x => new { x.TestCaseNameSnapshot, x.Outcome, x.DurationMs, x.FailureCategory }).ToListAsync())
            .OrderBy(x => x.TestCaseNameSnapshot, StringComparer.Ordinal)
            .Select(x => $"{x.TestCaseNameSnapshot}:{x.Outcome}:{x.DurationMs}:{x.FailureCategory}")));

    private static TestRun NewRun(Guid projectId, string label) => new()
    {
        ProjectId = projectId, RunLabel = label, TotalTests = 10, ConfigurationSnapshot = "{}"
    };

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition was not met within 30 seconds.");
            await Task.Delay(25);
        }
    }

    private sealed record PersistedCounts((int, int, int) ByOutcome, int DistinctNames, int CategorizedFailures);

    private sealed class FailingAfterFirstBatchWriter(IResultBatchWriter inner) : IResultBatchWriter
    {
        private int _calls;

        public Task WriteAsync(IReadOnlyList<TestCaseResult> batch, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _calls) == 1
                ? inner.WriteAsync(batch, cancellationToken)
                : throw new InvalidOperationException("Simulated database outage.");
    }

    private sealed class ThrowingProgressPublisher : IRunProgressPublisher
    {
        public Task PublishAsync(RunProgress progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated SignalR outage.");
    }
}
