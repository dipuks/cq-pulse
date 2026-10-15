using System.Diagnostics;
using System.Text.Json;
using CQPulse.Application.Analysis;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Integrations.LogTriage;
using CQPulse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static CQPulse.IntegrationTests.Infrastructure.ApiTestHelpers;

namespace CQPulse.IntegrationTests;

/// <summary>
/// Backpressure between finished runs and LogTriageAssistant: a full analysis queue must never block run completion, the
/// next run, an HTTP request or shutdown, and must never lose an analysis (it waits as Pending and is picked up later).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AnalysisQueueTests(ApiFixture fixture)
{
    private static readonly (string, string)[] SaturationSettings =
    [
        ("Integrations:LogTriage:QueueCapacity", "1"), ("Integrations:LogTriage:PendingSweepSeconds", "1"),
        ("Integrations:LogTriage:AutoAnalyzeFailedRuns", "true"), ("Integrations:LogTriage:AttemptTimeoutSeconds", "60")
    ];

    [Fact]
    public async Task Queue_never_waits_deduplicates_and_reports_what_it_holds()
    {
        var queue = new AnalysisQueue(Options.Create(new LogTriageOptions { QueueCapacity = 2 }));
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(queue.TryQueue(a));
        Assert.True(queue.TryQueue(a)); // already queued: not added twice
        Assert.True(queue.TryQueue(b));
        Assert.False(queue.TryQueue(c)); // full: returns at once instead of waiting
        Assert.Equal((2, false), (queue.Count, queue.IsQueuedOrRunning(c)));

        using var cancel = new CancellationTokenSource();
        await using var reader = queue.ReadAllAsync(cancel.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(a, reader.Current);
        Assert.True(queue.IsQueuedOrRunning(a)); // being processed
        Assert.True(queue.TryQueue(c));          // there is room again
        queue.Release(a);
        Assert.False(queue.IsQueuedOrRunning(a));
    }

    [Fact]
    public async Task A_full_queue_never_blocks_run_completion_and_waiting_analyses_run_when_there_is_room()
    {
        var lta = new FakeLogTriageHandler { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var factory = WithLogTriage(lta, SaturationSettings);
        using var client = await AdminClientAsync(factory);
        var project = await client.CreateProjectAsync();

        var stopwatch = Stopwatch.StartNew();
        var runs = new List<RunDetailDto>();
        for (var i = 0; i < 4; i++) runs.Add(await client.RunToCompletionAsync(NewRequest(project.Id, 20, seed: i + 1)));
        stopwatch.Stop();

        Assert.All(runs, r => Assert.Equal((RunStatus.CompletedWithFailures, true), (r.Status, r.FailedCount > 0)));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(20), $"Four small runs took {stopwatch.Elapsed} with LogTriageAssistant stalled.");
        // One analysis is in flight (held), one fills the queue, and two wait as Pending: explicit and durable, not dropped.
        var waiting = await WaitUntilAsync(() => LatestAnalysesAsync(client, runs),
            list => list.Count == 4 && list.Count(a => a.Status == AnalysisStatus.Running) == 1);
        Assert.Equal(1, waiting.Count(a => a.Status == AnalysisStatus.Pending && !a.WaitingForQueueSlot));
        Assert.Equal(2, waiting.Count(a => a.Status == AnalysisStatus.Pending && a.WaitingForQueueSlot));

        lta.Hold!.SetResult();
        var finished = await WaitUntilAsync(() => LatestAnalysesAsync(client, runs), list => list.Count == 4 && list.All(a => a.Status == AnalysisStatus.Succeeded));
        Assert.All(finished, a => Assert.False(a.WaitingForQueueSlot));
        Assert.Equal(4, lta.AnalyzeCalls);
    }

    [Fact]
    public async Task Shutdown_with_a_full_queue_is_prompt_and_waiting_analyses_resume_after_a_restart()
    {
        var stalled = new FakeLogTriageHandler { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var factory = WithLogTriage(stalled, SaturationSettings);
        List<Guid> analysisIds;
        try
        {
            using var client = await AdminClientAsync(factory);
            var project = await client.CreateProjectAsync();
            var runs = new List<RunDetailDto>();
            for (var i = 0; i < 3; i++) runs.Add(await client.RunToCompletionAsync(NewRequest(project.Id, 20, seed: 10 + i)));
            analysisIds = (await WaitUntilAsync(() => LatestAnalysesAsync(client, runs),
                    list => list.Count == 3 && list.Count(a => a.Status == AnalysisStatus.Running) == 1))
                .Select(a => a.Id).ToList();
        }
        finally
        {
            var stopwatch = Stopwatch.StartNew();
            await factory.DisposeAsync();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Shutdown took {stopwatch.Elapsed} with a saturated analysis queue.");
        }

        // Nothing is lost: the in-flight analysis is left Running or recorded Unavailable; the others are still Pending.
        var afterStop = await StatusesAsync(analysisIds);
        Assert.Equal(2, afterStop.Count(s => s == AnalysisStatus.Pending));
        Assert.Single(afterStop, s => s is AnalysisStatus.Running or AnalysisStatus.Unavailable);

        var healthy = new FakeLogTriageHandler();
        await using var restarted = WithLogTriage(healthy, SaturationSettings);
        using var restartedClient = await AdminClientAsync(restarted);
        var recovered = await WaitUntilAsync(() => Task.WhenAll(analysisIds.Select(id => restartedClient.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{id}"))),
            list => list.All(a => a.Status is AnalysisStatus.Succeeded or AnalysisStatus.Unavailable or AnalysisStatus.Failed));
        Assert.Equal(2, recovered.Count(a => a.Status == AnalysisStatus.Succeeded));
        var interrupted = Assert.Single(recovered, a => a.Status == AnalysisStatus.Unavailable);

        // The interrupted one is explicitly retryable and succeeds.
        await (await restartedClient.PostAsync($"/api/failure-analyses/{interrupted.Id}/retry", null)).ReadAsync<FailureAnalysisDto>();
        var retried = await WaitUntilAsync(() => restartedClient.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{interrupted.Id}"),
            a => a.Status is not (AnalysisStatus.Pending or AnalysisStatus.Running));
        Assert.Equal(AnalysisStatus.Succeeded, retried.Status);
    }

    [Fact]
    public async Task Startup_with_more_pending_analyses_than_the_queue_holds_does_not_deadlock()
    {
        using var setup = await fixture.CreateAdministratorClientAsync();
        var run = await setup.RunToCompletionAsync(NewRequest((await setup.CreateProjectAsync()).Id, 20));
        var ids = await fixture.WithDbContextAsync(async db =>
        {
            var pending = Enumerable.Range(0, 4).Select(i => NewPendingAnalysis(run.Id, DateTimeOffset.UtcNow.AddSeconds(i))).ToList();
            db.FailureAnalyses.AddRange(pending);
            await db.SaveChangesAsync();
            return pending.Select(x => x.Id).ToList();
        });

        var lta = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(lta, SaturationSettings);
        using var client = await AdminClientAsync(factory);

        var done = await WaitUntilAsync(() => Task.WhenAll(ids.Select(id => client.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{id}"))),
            list => list.All(a => a.Status == AnalysisStatus.Succeeded));
        Assert.Equal(4, done.Length);
    }

    /// <summary>A request cancelled after the analysis was saved, or any Pending analysis not in the queue, is picked up without a restart.</summary>
    [Fact]
    public async Task Pending_analyses_missing_from_the_queue_are_swept_up_while_running()
    {
        var lta = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(lta, SaturationSettings);
        using var client = await AdminClientAsync(factory);
        var run = await client.RunToCompletionAsync(NewRequest((await client.CreateProjectAsync()).Id, 20));
        var auto = await WaitUntilAsync(() => client.GetJsonAsync<List<FailureAnalysisDto>>($"/api/runs/{run.Id}/failure-analyses"),
            list => list.Count == 1 && list[0].Status == AnalysisStatus.Succeeded);

        var orphanId = await fixture.WithDbContextAsync(async db =>
        {
            var orphan = NewPendingAnalysis(run.Id, DateTimeOffset.UtcNow);
            db.FailureAnalyses.Add(orphan);
            await db.SaveChangesAsync();
            return orphan.Id;
        });

        var swept = await WaitUntilAsync(() => client.GetJsonAsync<FailureAnalysisDto>($"/api/failure-analyses/{orphanId}"),
            a => a.Status is not (AnalysisStatus.Pending or AnalysisStatus.Running));
        Assert.Equal(AnalysisStatus.Succeeded, swept.Status);
        Assert.Equal(2, lta.AnalyzeCalls);
        Assert.Single(auto);
    }

    [Fact]
    public async Task A_failing_completion_hook_leaves_runs_and_other_hooks_unaffected()
    {
        var lta = new FakeLogTriageHandler();
        await using var factory = WithLogTriage(lta, SaturationSettings, services => services.AddScoped<IRunCompletionObserver, ThrowingObserver>());
        using var client = await AdminClientAsync(factory);
        var project = await client.CreateProjectAsync();

        var first = await client.RunToCompletionAsync(NewRequest(project.Id, 20, seed: 1));
        var second = await client.RunToCompletionAsync(NewRequest(project.Id, 20, seed: 2));

        Assert.Equal((RunStatus.CompletedWithFailures, RunStatus.CompletedWithFailures), (first.Status, second.Status));
        var analyses = await WaitUntilAsync(() => LatestAnalysesAsync(client, [first, second]), list => list.Count == 2 && list.All(a => a.Status == AnalysisStatus.Succeeded));
        Assert.Equal(2, analyses.Count);
    }

    private sealed class ThrowingObserver : IRunCompletionObserver
    {
        public Task OnRunCompletedAsync(Guid runId, RunStatus status, int failedCount, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated hook failure.");
    }

    private static FailureAnalysis NewPendingAnalysis(Guid runId, DateTimeOffset requestedAt) => new()
    {
        TestRunId = runId, Provider = FailureAnalysisService.ProviderName, InputDigest = new string('a', 64),
        CorrelationId = Guid.NewGuid().ToString("N"), RequestedAtUtc = requestedAt
    };

    private Task<List<AnalysisStatus>> StatusesAsync(List<Guid> ids) =>
        fixture.WithDbContextAsync(db => db.FailureAnalyses.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => x.Status).ToListAsync());

    private static async Task<List<FailureAnalysisDto>> LatestAnalysesAsync(HttpClient client, IEnumerable<RunDetailDto> runs) =>
        (await Task.WhenAll(runs.Select(r => client.GetJsonAsync<List<FailureAnalysisDto>>($"/api/runs/{r.Id}/failure-analyses"))))
        .Where(list => list.Count > 0).Select(list => list[0]).ToList();

    private WebApplicationFactory<Program> WithLogTriage(
        FakeLogTriageHandler fake, (string Key, string Value)[] settings, Action<IServiceCollection>? services = null) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Integrations:LogTriage:BaseUrl", "https://logtriage.test");
            builder.UseSetting("Integrations:LogTriage:Username", FakeLogTriageHandler.Username);
            builder.UseSetting("Integrations:LogTriage:Password", FakeLogTriageHandler.Password);
            builder.UseSetting("Integrations:LogTriage:RetryCount", "1");
            builder.UseSetting("Integrations:LogTriage:RetryBaseDelayMilliseconds", "10");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureTestServices(collection =>
            {
                collection.AddHttpClient<LogTriageClient>().ConfigurePrimaryHttpMessageHandler(() => fake);
                services?.Invoke(collection);
            });
        });

    private async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new("Bearer", await fixture.GetTokenAsync(fixture.Administrator));
        return client;
    }

    private static async Task<T> WaitUntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (true)
        {
            var value = await read();
            if (done(value)) return value;
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Condition not met; last value: {JsonSerializer.Serialize(value)}");
            await Task.Delay(100);
        }
    }
}
