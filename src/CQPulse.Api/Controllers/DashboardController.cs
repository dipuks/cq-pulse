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
    [HttpGet("summary")]
    public async Task<ActionResult<object>> GetSummary(CancellationToken cancellationToken)
    {
        var totals = await dbContext.TestRuns.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalRuns = group.Count(),
                Passed = group.Sum(x => x.PassedCount),
                Failed = group.Sum(x => x.FailedCount),
                Skipped = group.Sum(x => x.SkippedCount),
                ActiveRuns = group.Count(x => x.Status == Domain.RunStatus.Queued || x.Status == Domain.RunStatus.Running)
            })
            .SingleOrDefaultAsync(cancellationToken);
        var completed = (totals?.Passed ?? 0) + (totals?.Failed ?? 0) + (totals?.Skipped ?? 0);
        return Ok(new
        {
            ProjectCount = await dbContext.Projects.CountAsync(cancellationToken),
            TestSuiteCount = await dbContext.TestSuites.CountAsync(cancellationToken),
            TestCaseCount = await dbContext.TestCases.CountAsync(cancellationToken),
            totals?.TotalRuns,
            totals?.Passed,
            totals?.Failed,
            totals?.Skipped,
            totals?.ActiveRuns,
            PassRate = completed == 0 ? 0 : Math.Round((double)(totals?.Passed ?? 0) / completed * 100, 1)
        });
    }
}
