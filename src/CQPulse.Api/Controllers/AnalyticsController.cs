using CQPulse.Application.Analytics;
using CQPulse.Domain;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

/// <summary>Aggregates over persisted test-case results, filtered by <c>CompletedAtUtc</c>.</summary>
[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/analytics")]
public sealed class AnalyticsController(CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<AnalyticsSummaryDto> Summary(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var totals = await FilterResults(fromUtc, toUtc, projectId).GroupBy(_ => 1).Select(group => new
        {
            Passed = group.Count(x => x.Outcome == TestOutcome.Passed),
            Failed = group.Count(x => x.Outcome == TestOutcome.Failed),
            Skipped = group.Count(x => x.Outcome == TestOutcome.Skipped),
            AverageDurationMs = group.Average(x => (double)x.DurationMs)
        }).SingleOrDefaultAsync(cancellationToken);
        if (totals is null) return new AnalyticsSummaryDto(0, 0, 0, 0, 0, 0, null);

        var total = totals.Passed + totals.Failed + totals.Skipped;
        return new AnalyticsSummaryDto(
            total, totals.Passed, totals.Failed, totals.Skipped,
            DashboardController.Percentage(totals.Passed, total),
            DashboardController.Percentage(totals.Failed, total),
            totals.AverageDurationMs);
    }

    [HttpGet("execution-trends")]
    public async Task<IReadOnlyList<TrendPointDto>> Trends(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var rows = await FilterResults(fromUtc, toUtc, projectId)
            .GroupBy(x => x.CompletedAtUtc.Date)
            .OrderBy(x => x.Key)
            .Select(group => new
            {
                Date = group.Key,
                Passed = group.Count(x => x.Outcome == TestOutcome.Passed),
                Failed = group.Count(x => x.Outcome == TestOutcome.Failed),
                Skipped = group.Count(x => x.Outcome == TestOutcome.Skipped),
                AverageDurationMs = group.Average(x => (double)x.DurationMs)
            }).ToListAsync(cancellationToken);
        return rows.Select(x => new TrendPointDto(DateOnly.FromDateTime(x.Date), x.Passed, x.Failed, x.Skipped, x.AverageDurationMs)).ToList();
    }

    [HttpGet("failure-breakdown")]
    public async Task<IReadOnlyList<FailureCategoryCountDto>> FailureBreakdown(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var rows = await FilterResults(fromUtc, toUtc, projectId)
            .Where(x => x.Outcome == TestOutcome.Failed && x.FailureCategory != null)
            .GroupBy(x => x.FailureCategory!.Value)
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Category)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new FailureCategoryCountDto(x.Category, x.Count)).ToList();
    }

    private IQueryable<TestCaseResult> FilterResults(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, Guid? projectId)
    {
        var query = dbContext.TestCaseResults.AsNoTracking().AsQueryable();
        if (fromUtc is not null) query = query.Where(x => x.CompletedAtUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(x => x.CompletedAtUtc <= toUtc);
        if (projectId is not null) query = query.Where(x => x.TestRun.ProjectId == projectId);
        return query;
    }
}
