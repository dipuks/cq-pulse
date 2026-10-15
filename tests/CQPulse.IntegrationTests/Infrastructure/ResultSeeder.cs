using CQPulse.Domain;
using CQPulse.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.IntegrationTests.Infrastructure;

/// <summary>A persisted result row at a controlled time, for tests that need data the simulator cannot produce.</summary>
public sealed record SeedResult(DateTimeOffset At, TestOutcome Outcome, FailureCategory? Category = null, string? Message = null);

public static class ResultSeeder
{
    /// <summary>Inserts a completed run whose counters match <paramref name="rows"/>, saved through EF like real results.</summary>
    public static Task<Guid> InsertCompletedRunAsync(this ApiFixture fixture, Guid projectId, IReadOnlyList<SeedResult> rows, string label = "Seeded run") =>
        fixture.WithDbContextAsync(async db =>
        {
            var run = new TestRun
            {
                ProjectId = projectId, RunLabel = label, ConfigurationSnapshot = "{}", TotalTests = rows.Count, CompletedTests = rows.Count,
                PassedCount = rows.Count(x => x.Outcome == TestOutcome.Passed), FailedCount = rows.Count(x => x.Outcome == TestOutcome.Failed),
                SkippedCount = rows.Count(x => x.Outcome == TestOutcome.Skipped)
            };
            var first = rows.Count == 0 ? DateTimeOffset.UtcNow : rows.Min(x => x.At);
            var last = rows.Count == 0 ? DateTimeOffset.UtcNow : rows.Max(x => x.At);
            run.Start(first.AddSeconds(-1));
            run.Complete(last.AddSeconds(1));
            db.TestRuns.Add(run);
            db.TestCaseResults.AddRange(rows.Select((r, i) => new TestCaseResult
            {
                TestRunId = run.Id, TestCaseNameSnapshot = $"Seeded test {i}", Outcome = r.Outcome, DurationMs = 1,
                StartedAtUtc = r.At, CompletedAtUtc = r.At, FailureCategory = r.Category, ErrorMessage = r.Message
            }));
            await db.SaveChangesAsync();
            return run.Id;
        });

    /// <summary>Simulates rows saved before the signature column existed.</summary>
    public static Task<int> ClearSignaturesAsync(this ApiFixture fixture, Guid runId) =>
        fixture.WithDbContextAsync(db => db.TestCaseResults.Where(x => x.TestRunId == runId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Signature, (string?)null)));

    /// <summary>Removes seeded runs (results cascade) so the shared database stays small for later tests.</summary>
    public static Task<int> DeleteRunsAsync(this ApiFixture fixture, params Guid[] runIds) =>
        fixture.WithDbContextAsync(async db =>
        {
            await db.FailureAnalyses.Where(x => runIds.Contains(x.TestRunId)).ExecuteDeleteAsync();
            await db.DecisionEvaluations.Where(x => runIds.Contains(x.TestRunId)).ExecuteDeleteAsync();
            await db.ExecutionLogs.Where(x => runIds.Contains(x.TestRunId)).ExecuteDeleteAsync();
            await db.TestCaseResults.Where(x => runIds.Contains(x.TestRunId)).ExecuteDeleteAsync();
            return await db.TestRuns.Where(x => runIds.Contains(x.Id)).ExecuteDeleteAsync();
        });
}
