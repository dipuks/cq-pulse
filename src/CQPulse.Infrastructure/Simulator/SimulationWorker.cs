using System.Text.Json;
using System.Threading.Channels;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LogLevel = CQPulse.Domain.LogLevel;
using Microsoft.Extensions.Options;

namespace CQPulse.Infrastructure.Simulator;

/// <summary>
/// Executes queued runs one at a time: tests run with bounded concurrency, results flow through a bounded
/// channel (backpressure) to batched persistence, and every run ends in a terminal state whose counters are
/// computed from the persisted rows. On application shutdown an in-flight run is left for
/// <see cref="ExecutionRecoveryService"/> to mark failed at the next startup.
/// </summary>
internal sealed class SimulationWorker(
    SimulationQueue queue,
    RunCancellationRegistry cancellations,
    IServiceScopeFactory scopeFactory,
    IResultBatchWriter resultWriter,
    IOptions<SimulatorOptions> options,
    ILogger<SimulationWorker> logger) : BackgroundService
{
    private const int MaxSummaryLength = 1_000;
    private const int MaxSaveAttempts = 10;
    private SimulatorOptions Settings => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var runId in queue.ReadAllAsync(stoppingToken))
            {
                using var _ = logger.BeginScope(new Dictionary<string, object> { ["RunId"] = runId });
                try
                {
                    await ProcessAsync(runId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Shutdown interrupted simulation run {RunId}; startup recovery will mark it failed.", runId);
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Simulation run {RunId} crashed.", runId);
                    await TryMarkFailedAsync(runId, Summarize("Simulation failed unexpectedly", exception));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task ProcessAsync(Guid runId, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IRunProgressPublisher>();

        var run = await db.TestRuns.SingleOrDefaultAsync(x => x.Id == runId, stoppingToken);
        if (run is null)
        {
            logger.LogWarning("Queued run {RunId} no longer exists.", runId);
            return;
        }
        if (run.Status != RunStatus.Queued) return; // e.g. cancelled while waiting in the queue

        var request = TryReadConfiguration(run.ConfigurationSnapshot);
        List<CatalogCase>? cases = null;
        if (request?.WorkloadSource == WorkloadSource.Catalog)
            cases = await CatalogWorkloadLoader.LoadAsync(db, request.ProjectId, request.TestSuiteId, stoppingToken);
        var unrunnable = request is null ? "The run configuration could not be read."
            : cases is { Count: 0 } ? "The target has no enabled test cases (they were disabled or deleted after the run was queued)."
            : null;
        if (request is null || unrunnable is not null)
        {
            unrunnable ??= "The run configuration could not be read.";
            run.Fail(DateTimeOffset.UtcNow, unrunnable);
            await db.SaveChangesAsync(CancellationToken.None);
            await WriteLogAsync(db, runId, LogLevel.Error, unrunnable);
            await PublishSafelyAsync(publisher, ToProgress(run, null));
            return;
        }

        run.Start(DateTimeOffset.UtcNow);
        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Cancelled between dequeue and start: the run was changed by another request, so do not execute it.
            logger.LogInformation("Simulation run {RunId} changed before it started (likely cancelled); skipping.", runId);
            return;
        }
        await PublishSafelyAsync(publisher, ToProgress(run, null));
        await WriteLogAsync(db, runId, LogLevel.Information,
            $"Run started: {request.TestCount:N0} tests ({(cases is null ? "synthetic" : $"{cases.Count:N0} catalog cases")}), concurrency {request.MaxConcurrency}, seed {request.RandomSeed?.ToString() ?? "random"}.");
        logger.LogInformation("Simulation run {RunId} started: {TestCount} tests, concurrency {Concurrency}.",
            runId, request.TestCount, request.MaxConcurrency);

        var (executionFailure, persistenceFailure) = await ExecuteAsync(run, request, cases, publisher, stoppingToken);
        stoppingToken.ThrowIfCancellationRequested();

        await FinishAsync(db, publisher, runId, executionFailure, persistenceFailure);
    }

    private async Task<(Exception? Execution, Exception? Persistence)> ExecuteAsync(
        TestRun run, SimulationRequest request, IReadOnlyList<CatalogCase>? cases, IRunProgressPublisher publisher, CancellationToken stoppingToken)
    {
        var runToken = cancellations.Register(run.Id, stoppingToken);
        try
        {
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(runToken);
            var results = Channel.CreateBounded<TestCaseResult>(new BoundedChannelOptions(Settings.ResultBufferCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });
            var persistence = PersistResultsAsync(results.Reader, execution, stoppingToken);

            var workload = new SimulationWorkload(request, request.RandomSeed ?? Random.Shared.Next(), cases);
            var counters = new int[3];
            var completed = 0;
            var progressStep = Math.Max(1, run.TotalTests / Settings.ProgressUpdatesPerRun);

            Exception? executionFailure = null;
            try
            {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, run.TotalTests),
                    new ParallelOptions { MaxDegreeOfParallelism = request.MaxConcurrency, CancellationToken = execution.Token },
                    async (index, token) =>
                    {
                        var test = workload.Generate(index);
                        var startedAt = DateTimeOffset.UtcNow;
                        await Task.Delay(test.DurationMs, token);
                        var name = test.Name;
                        await results.Writer.WriteAsync(new TestCaseResult
                        {
                            TestRunId = run.Id,
                            TestCaseId = test.TestCaseId,
                            TestCaseNameSnapshot = name,
                            Outcome = test.Outcome,
                            StartedAtUtc = startedAt,
                            CompletedAtUtc = DateTimeOffset.UtcNow,
                            DurationMs = test.DurationMs,
                            ErrorMessage = test.Outcome == TestOutcome.Failed ? $"Simulated {test.FailureCategory} failure." : null,
                            FailureCategory = test.FailureCategory
                        }, token);

                        Interlocked.Increment(ref counters[(int)test.Outcome]);
                        var current = Interlocked.Increment(ref completed);
                        if (current == run.TotalTests || current % progressStep == 0)
                        {
                            await PublishSafelyAsync(publisher, new RunProgress(run.Id, RunStatus.Running, run.TotalTests, current,
                                Volatile.Read(ref counters[0]), Volatile.Read(ref counters[1]), Volatile.Read(ref counters[2]), name));
                        }
                    });
            }
            catch (Exception exception)
            {
                executionFailure = exception;
            }
            finally
            {
                results.Writer.TryComplete();
            }

            Exception? persistenceFailure = null;
            try
            {
                await persistence;
            }
            catch (Exception exception)
            {
                persistenceFailure = exception;
            }

            return (executionFailure, persistenceFailure);
        }
        finally
        {
            cancellations.Remove(run.Id);
        }
    }

    /// <summary>Drains results in batches. A failure cancels execution so producers never block on a full channel.</summary>
    private async Task PersistResultsAsync(
        ChannelReader<TestCaseResult> reader, CancellationTokenSource execution, CancellationToken stoppingToken)
    {
        try
        {
            var batch = new List<TestCaseResult>(Settings.ResultBatchSize);
            await foreach (var result in reader.ReadAllAsync(stoppingToken))
            {
                batch.Add(result);
                if (batch.Count < Settings.ResultBatchSize) continue;
                await resultWriter.WriteAsync(batch.ToArray(), stoppingToken);
                batch.Clear();
            }

            if (batch.Count > 0) await resultWriter.WriteAsync(batch.ToArray(), stoppingToken);
        }
        catch
        {
            await execution.CancelAsync();
            throw;
        }
    }

    private async Task FinishAsync(
        CQPulseDbContext db, IRunProgressPublisher publisher, Guid runId, Exception? executionFailure, Exception? persistenceFailure)
    {
        if (persistenceFailure is not null)
            logger.LogError(persistenceFailure, "Result persistence failed for simulation run {RunId}.", runId);
        else if (executionFailure is not null)
            logger.LogError(executionFailure, "Simulation run {RunId} failed during execution.", runId);

        // The terminal decision is re-made from fresh state on every attempt: a cancellation saved by another
        // request (detected through the rowversion token) always wins over completion.
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var run = await db.TestRuns.SingleAsync(x => x.Id == runId, CancellationToken.None);
            await ApplyPersistedCountsAsync(db, run);

            var now = DateTimeOffset.UtcNow;
            string logMessage;
            var level = LogLevel.Information;
            if (run.Status == RunStatus.Cancelled)
            {
                logMessage = $"Run cancelled after {run.CompletedTests:N0} of {run.TotalTests:N0} tests.";
            }
            else if (persistenceFailure is not null)
            {
                run.Fail(now, Summarize("Result persistence failed", persistenceFailure));
                (logMessage, level) = (run.ErrorSummary!, LogLevel.Error);
            }
            else if (executionFailure is not null)
            {
                run.Fail(now, Summarize("Simulation execution failed", executionFailure));
                (logMessage, level) = (run.ErrorSummary!, LogLevel.Error);
            }
            else
            {
                run.Complete(now);
                logMessage = $"Run completed with status {run.Status}: {run.PassedCount:N0} passed, {run.FailedCount:N0} failed, {run.SkippedCount:N0} skipped.";
            }

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
            {
                logger.LogInformation("Simulation run {RunId} changed concurrently while finishing; re-evaluating.", runId);
                continue;
            }

            logger.LogInformation("Simulation run {RunId} finished with status {Status}.", runId, run.Status);
            await WriteLogAsync(db, runId, level, logMessage);
            await PublishSafelyAsync(publisher, ToProgress(run, null));
            return;
        }
    }

    /// <summary>Run-level diagnostic entry; failures to log never affect the run itself.</summary>
    private async Task WriteLogAsync(CQPulseDbContext db, Guid runId, LogLevel level, string message)
    {
        try
        {
            db.ExecutionLogs.Add(new ExecutionLog { TestRunId = runId, Level = level, Message = message });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not write execution log for run {RunId}.", runId);
            db.ChangeTracker.Clear();
        }
    }

    private async Task TryMarkFailedAsync(Guid runId, string summary)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
            for (var attempt = 1; ; attempt++)
            {
                db.ChangeTracker.Clear();
                var run = await db.TestRuns.SingleOrDefaultAsync(x => x.Id == runId);
                if (run is null || run.Status.IsTerminal()) return;
                await ApplyPersistedCountsAsync(db, run);
                run.Fail(DateTimeOffset.UtcNow, summary);
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
                {
                    continue;
                }
                await WriteLogAsync(db, runId, LogLevel.Error, summary);
                await PublishSafelyAsync(scope.ServiceProvider.GetRequiredService<IRunProgressPublisher>(), ToProgress(run, null));
                return;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not mark simulation run {RunId} as failed; startup recovery will handle it.", runId);
        }
    }

    /// <summary>Counters always reflect what was actually persisted, including for partial runs.</summary>
    private static async Task ApplyPersistedCountsAsync(CQPulseDbContext db, TestRun run)
    {
        var counts = await db.TestCaseResults.Where(x => x.TestRunId == run.Id)
            .GroupBy(x => x.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Outcome, x => x.Count);
        run.PassedCount = counts.GetValueOrDefault(TestOutcome.Passed);
        run.FailedCount = counts.GetValueOrDefault(TestOutcome.Failed);
        run.SkippedCount = counts.GetValueOrDefault(TestOutcome.Skipped);
        run.CompletedTests = run.PassedCount + run.FailedCount + run.SkippedCount;
    }

    private async Task PublishSafelyAsync(IRunProgressPublisher publisher, RunProgress progress)
    {
        try
        {
            await publisher.PublishAsync(progress, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Live updates are best-effort; clients reconcile through REST.
            logger.LogWarning(exception, "Progress notification for run {RunId} failed.", progress.RunId);
        }
    }

    private static SimulationRequest? TryReadConfiguration(string snapshot)
    {
        try
        {
            return JsonSerializer.Deserialize<SimulationRequest>(snapshot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RunProgress ToProgress(TestRun run, string? currentTest) =>
        new(run.Id, run.Status, run.TotalTests, run.CompletedTests, run.PassedCount, run.FailedCount, run.SkippedCount, currentTest);

    private static string Summarize(string prefix, Exception exception)
    {
        var summary = $"{prefix}: {exception.GetBaseException().Message}";
        return summary.Length <= MaxSummaryLength ? summary : summary[..MaxSummaryLength];
    }
}
