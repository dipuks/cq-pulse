using CQPulse.Application.Runs;
using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/dashboard")]
public sealed class DashboardController(CQPulseDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Catalog counts are unique items (projects, suites, test cases); Passed/Failed/Skipped are
    /// historical executions summed over all runs.
    /// </summary>
    [HttpGet("summary")]
    public async Task<DashboardSummaryDto> GetSummary(CancellationToken cancellationToken)
    {
        var totals = await dbContext.TestRuns.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalRuns = group.Count(),
                Passed = group.Sum(x => x.PassedCount),
                Failed = group.Sum(x => x.FailedCount),
                Skipped = group.Sum(x => x.SkippedCount),
                ActiveRuns = group.Count(x => x.Status == RunStatus.Queued || x.Status == RunStatus.Running)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var passed = totals?.Passed ?? 0;
        var executed = passed + (totals?.Failed ?? 0) + (totals?.Skipped ?? 0);
        return new DashboardSummaryDto(
            await dbContext.Projects.CountAsync(cancellationToken),
            await dbContext.TestSuites.CountAsync(cancellationToken),
            await dbContext.TestCases.CountAsync(cancellationToken),
            totals?.TotalRuns ?? 0,
            passed,
            totals?.Failed ?? 0,
            totals?.Skipped ?? 0,
            totals?.ActiveRuns ?? 0,
            Percentage(passed, executed));
    }

    internal static double Percentage(int part, int total) => total == 0 ? 0 : Math.Round((double)part / total * 100, 1);
}
