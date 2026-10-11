using System.Collections.Concurrent;
using System.Text.Json;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CQPulse.Infrastructure.Simulator;

internal sealed class SimulationWorker(
    SimulationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<SimulationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var runId in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(runId, stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Simulation run {RunId} crashed.", runId); }
        }
    }

    private async Task ProcessAsync(Guid runId, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRunProgressPublisher>();
        var run = await db.TestRuns.SingleAsync(x => x.Id == runId, stoppingToken);
        if (run.Status != RunStatus.Queued) return;

        var request = JsonSerializer.Deserialize<SimulationRequest>(run.ConfigurationSnapshot)
            ?? throw new InvalidOperationException("Run configuration is invalid.");
        run.Start(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(stoppingToken);

        var results = new ConcurrentBag<TestCaseResult>();
        var counters = new int[3];
        await Parallel.ForEachAsync(
            Enumerable.Range(1, run.TotalTests),
            new ParallelOptions { MaxDegreeOfParallelism = request.MaxConcurrency, CancellationToken = stoppingToken },
            async (index, cancellationToken) =>
            {
                var random = request.RandomSeed is { } seed
                    ? new Random(HashCode.Combine(seed, index))
                    : Random.Shared;
                var duration = random.Next(request.MinimumDurationMs, request.MaximumDurationMs + 1);
                await Task.Delay(duration, cancellationToken);
                var roll = random.Next(100);
                var outcome = roll < request.PassPercentage ? TestOutcome.Passed :
                    roll < request.PassPercentage + request.FailurePercentage ? TestOutcome.Failed : TestOutcome.Skipped;
                Interlocked.Increment(ref counters[(int)outcome]);
                results.Add(new TestCaseResult
                {
                    TestRunId = run.Id,
                    TestCaseNameSnapshot = $"Generated test {index:N0}",
                    Outcome = outcome,
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-duration),
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    DurationMs = duration,
                    ErrorMessage = outcome == TestOutcome.Failed ? "Simulated failure." : null,
                    FailureCategory = outcome == TestOutcome.Failed ? FailureCategory.UiElement : null
                });
            });

        var trackedRun = await db.TestRuns.SingleAsync(x => x.Id == runId, stoppingToken);
        if (trackedRun.Status == RunStatus.Cancelled) return;
        db.TestCaseResults.AddRange(results);
        trackedRun.CompletedTests = trackedRun.TotalTests;
        trackedRun.PassedCount = counters[(int)TestOutcome.Passed];
        trackedRun.FailedCount = counters[(int)TestOutcome.Failed];
        trackedRun.SkippedCount = counters[(int)TestOutcome.Skipped];
        trackedRun.Complete(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(stoppingToken);
        await publisher.PublishAsync(new RunProgress(
            runId, trackedRun.Status, trackedRun.TotalTests, trackedRun.CompletedTests,
            trackedRun.PassedCount, trackedRun.FailedCount, trackedRun.SkippedCount, null), stoppingToken);
    }
}
