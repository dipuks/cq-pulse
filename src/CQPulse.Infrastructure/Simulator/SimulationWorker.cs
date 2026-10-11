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
    RunCancellationRegistry cancellations,
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
        var cancellationToken = cancellations.Register(runId, stoppingToken);
        var results = new ConcurrentBag<TestCaseResult>();
        var counters = new int[3];
        var completed = 0;
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(1, run.TotalTests),
                new ParallelOptions { MaxDegreeOfParallelism = request.MaxConcurrency, CancellationToken = cancellationToken },
                async (index, token) =>
                {
                    var random = request.RandomSeed is { } seed ? new Random(HashCode.Combine(seed, index)) : Random.Shared;
                    var duration = random.Next(request.MinimumDurationMs, request.MaximumDurationMs + 1);
                    await Task.Delay(duration, token);
                    var roll = random.Next(100);
                    var outcome = roll < request.PassPercentage ? TestOutcome.Passed :
                        roll < request.PassPercentage + request.FailurePercentage ? TestOutcome.Failed : TestOutcome.Skipped;
                    var category = outcome == TestOutcome.Failed
                        ? request.FailureCategories![random.Next(request.FailureCategories.Count)] : (FailureCategory?)null;
                    Interlocked.Increment(ref counters[(int)outcome]);
                    var current = Interlocked.Increment(ref completed);
                    results.Add(new TestCaseResult
                    {
                        TestRunId = run.Id, TestCaseNameSnapshot = $"Generated test {index:N0}", Outcome = outcome,
                        StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-duration), CompletedAtUtc = DateTimeOffset.UtcNow,
                        DurationMs = duration, ErrorMessage = outcome == TestOutcome.Failed ? $"Simulated {category} failure." : null,
                        FailureCategory = category
                    });
                    if (current == run.TotalTests || current % Math.Max(1, run.TotalTests / 100) == 0)
                    {
                        await publisher.PublishAsync(new RunProgress(runId, RunStatus.Running, run.TotalTests, current,
                            counters[(int)TestOutcome.Passed], counters[(int)TestOutcome.Failed], counters[(int)TestOutcome.Skipped],
                            $"Generated test {index:N0}"), token);
                    }
                });

            db.TestCaseResults.AddRange(results);
            run.CompletedTests = run.TotalTests;
            run.PassedCount = counters[(int)TestOutcome.Passed];
            run.FailedCount = counters[(int)TestOutcome.Failed];
            run.SkippedCount = counters[(int)TestOutcome.Skipped];
            run.Complete(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(stoppingToken);
            await publisher.PublishAsync(new RunProgress(runId, run.Status, run.TotalTests, run.CompletedTests,
                run.PassedCount, run.FailedCount, run.SkippedCount, null), stoppingToken);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var cancelledRun = await db.TestRuns.SingleAsync(x => x.Id == runId, CancellationToken.None);
            await publisher.PublishAsync(new RunProgress(runId, cancelledRun.Status, cancelledRun.TotalTests, completed,
                counters[(int)TestOutcome.Passed], counters[(int)TestOutcome.Failed], counters[(int)TestOutcome.Skipped], null), CancellationToken.None);
        }
        finally { cancellations.Remove(runId); }
    }
}
