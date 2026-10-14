using CQPulse.Application.Runs;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

/// <summary>Run projections shared by the runs and simulator endpoints.</summary>
internal static class RunQueries
{
    public static Task<RunDetailDto?> GetDetailAsync(CQPulseDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        dbContext.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new RunDetailDto(
                x.Id, x.ProjectId, x.TestSuiteId, x.RunLabel, x.Status, x.RequestedAtUtc, x.StartedAtUtc, x.CompletedAtUtc,
                x.TotalTests, x.CompletedTests, x.PassedCount, x.FailedCount, x.SkippedCount, x.ConfigurationSnapshot,
                x.RandomSeed, x.ErrorSummary, x.CancellationReason,
                x.StartedAtUtc != null && x.CompletedAtUtc != null
                    ? EF.Functions.DateDiffMillisecond(x.StartedAtUtc.Value, x.CompletedAtUtc.Value)
                    : null))
            .SingleOrDefaultAsync(cancellationToken);
}
