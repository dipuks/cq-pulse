using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CQPulse.Infrastructure.Simulator;

/// <summary>Persists one batch of simulated results for a single run. A seam so tests can inject persistence failures.</summary>
internal interface IResultBatchWriter
{
    Task WriteAsync(IReadOnlyList<TestCaseResult> batch, CancellationToken cancellationToken);
}

/// <summary>
/// Saves each batch with a short-lived DbContext (the change tracker never grows with the run) and, in the same
/// transaction, increments the run's progress counters so REST polling sees live progress that always equals the
/// persisted rows. Only counter columns are updated, so a concurrent cancellation status is never overwritten.
/// </summary>
internal sealed class EfResultBatchWriter(IServiceScopeFactory scopeFactory) : IResultBatchWriter
{
    public async Task WriteAsync(IReadOnlyList<TestCaseResult> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0) return;
        var runId = batch[0].TestRunId;
        var passed = batch.Count(x => x.Outcome == TestOutcome.Passed);
        var failed = batch.Count(x => x.Outcome == TestOutcome.Failed);
        var skipped = batch.Count - passed - failed;

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CQPulseDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.TestCaseResults.AddRange(batch);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.TestRuns.Where(x => x.Id == runId).ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.CompletedTests, x => x.CompletedTests + batch.Count)
            .SetProperty(x => x.PassedCount, x => x.PassedCount + passed)
            .SetProperty(x => x.FailedCount, x => x.FailedCount + failed)
            .SetProperty(x => x.SkippedCount, x => x.SkippedCount + skipped), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
