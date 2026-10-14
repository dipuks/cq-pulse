using System.Linq.Expressions;
using CQPulse.Application.Runs;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

/// <summary>Run projections shared by the runs, projects and simulator endpoints.</summary>
internal static class RunQueries
{
    public static readonly Expression<Func<TestRun, RunSummaryDto>> Summary = x => new RunSummaryDto(
        x.Id, x.ProjectId, x.TestSuiteId, x.RunLabel, x.Status, x.RequestedAtUtc, x.StartedAtUtc,
        x.CompletedAtUtc, x.TotalTests, x.CompletedTests, x.PassedCount, x.FailedCount, x.SkippedCount, x.ErrorSummary,
        x.RerunOfRunId);

    public static Task<RunDetailDto?> GetDetailAsync(CQPulseDbContext dbContext, Guid runId, CancellationToken cancellationToken) =>
        dbContext.TestRuns.AsNoTracking().Where(x => x.Id == runId)
            .Select(x => new RunDetailDto(
                x.Id, x.ProjectId, x.TestSuiteId, x.RunLabel, x.Status, x.RequestedAtUtc, x.StartedAtUtc, x.CompletedAtUtc,
                x.TotalTests, x.CompletedTests, x.PassedCount, x.FailedCount, x.SkippedCount, x.ConfigurationSnapshot,
                x.RandomSeed, x.ErrorSummary, x.CancellationReason,
                x.StartedAtUtc != null && x.CompletedAtUtc != null
                    ? EF.Functions.DateDiffMillisecond(x.StartedAtUtc.Value, x.CompletedAtUtc.Value)
                    : null,
                x.RerunOfRunId,
                dbContext.TestRuns.Where(r => r.RerunOfRunId == x.Id).OrderBy(r => r.RequestedAtUtc).Select(r => r.Id).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
}
