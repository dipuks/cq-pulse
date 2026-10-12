using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/analytics")]
public sealed class AnalyticsController(CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<object>> Summary(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var results = FilterResults(fromUtc, toUtc, projectId);
        var totals = await results.GroupBy(_ => 1).Select(group => new
        {
            Passed = group.Count(x => x.Outcome == TestOutcome.Passed),
            Failed = group.Count(x => x.Outcome == TestOutcome.Failed),
            Skipped = group.Count(x => x.Outcome == TestOutcome.Skipped),
            AverageDurationMs = group.Average(x => (double)x.DurationMs)
        }).SingleOrDefaultAsync(cancellationToken);
        var total = (totals?.Passed ?? 0) + (totals?.Failed ?? 0) + (totals?.Skipped ?? 0);
        return Ok(new
        {
            Total = total,
            totals?.Passed,
            totals?.Failed,
            totals?.Skipped,
            PassRate = total == 0 ? 0 : Math.Round((double)(totals?.Passed ?? 0) / total * 100, 1),
            FailureRate = total == 0 ? 0 : Math.Round((double)(totals?.Failed ?? 0) / total * 100, 1),
            totals?.AverageDurationMs
        });
    }

    [HttpGet("execution-trends")]
    public async Task<ActionResult<object>> Trends(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var results = FilterResults(fromUtc, toUtc, projectId);
        var items = await results.GroupBy(x => x.CompletedAtUtc.Date)
            .OrderBy(x => x.Key)
            .Select(group => new
            {
                Date = group.Key,
                Passed = group.Count(x => x.Outcome == TestOutcome.Passed),
                Failed = group.Count(x => x.Outcome == TestOutcome.Failed),
                Skipped = group.Count(x => x.Outcome == TestOutcome.Skipped),
                AverageDurationMs = group.Average(x => (double)x.DurationMs)
            }).ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("failure-breakdown")]
    public async Task<ActionResult<object>> FailureBreakdown(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var failures = FilterResults(fromUtc, toUtc, projectId)
            .Where(x => x.Outcome == TestOutcome.Failed && x.FailureCategory != null);
        var items = await failures.GroupBy(x => x.FailureCategory)
            .OrderByDescending(x => x.Count())
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    private IQueryable<CQPulse.Domain.Entities.TestCaseResult> FilterResults(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId)
    {
        var query = dbContext.TestCaseResults.AsNoTracking().AsQueryable();
        if (fromUtc is not null) query = query.Where(x => x.CompletedAtUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(x => x.CompletedAtUtc <= toUtc);
        if (projectId is not null) query = query.Where(x => x.TestRun.ProjectId == projectId);
        return query;
    }
}
