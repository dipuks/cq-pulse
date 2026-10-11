using CQPulse.Application.Catalog;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/runs")]
public sealed class RunsController(CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<PageResponse<object>> GetAll(Guid? projectId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.TestRuns.AsNoTracking();
        if (projectId is not null) query = query.Where(x => x.ProjectId == projectId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.RequestedAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => (object)new
            {
                x.Id, x.ProjectId, x.TestSuiteId, x.RunLabel, x.Status, x.RequestedAtUtc, x.StartedAtUtc,
                x.CompletedAtUtc, x.TotalTests, x.CompletedTests, x.PassedCount, x.FailedCount, x.SkippedCount, x.ErrorSummary
            }).ToListAsync(cancellationToken);
        return new PageResponse<object>(items, total, normalizedPage, normalizedSize);
    }

    [HttpGet("{runId:guid}/results")]
    public async Task<PageResponse<object>> GetResults(Guid runId, string? outcome, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.TestCaseResults.AsNoTracking().Where(x => x.TestRunId == runId);
        if (!string.IsNullOrWhiteSpace(outcome) && Enum.TryParse<CQPulse.Domain.TestOutcome>(outcome, true, out var parsed))
            query = query.Where(x => x.Outcome == parsed);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CompletedAtUtc)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => (object)new
            {
                x.Id, x.TestCaseNameSnapshot, x.Outcome, x.DurationMs, x.StartedAtUtc, x.CompletedAtUtc,
                x.ErrorMessage, x.FailureCategory
            }).ToListAsync(cancellationToken);
        return new PageResponse<object>(items, total, normalizedPage, normalizedSize);
    }

    [HttpGet("{runId:guid}/logs")]
    public async Task<PageResponse<object>> GetLogs(Guid runId, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.ExecutionLogs.AsNoTracking().Where(x => x.TestRunId == runId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.TimestampUtc)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => (object)new { x.Id, x.TimestampUtc, x.Level, x.Message, x.MetadataJson }).ToListAsync(cancellationToken);
        return new PageResponse<object>(items, total, normalizedPage, normalizedSize);
    }
}
