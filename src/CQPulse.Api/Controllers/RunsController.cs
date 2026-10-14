using CQPulse.Application.Analysis;
using CQPulse.Application.Catalog;
using System.Security.Claims;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/runs")]
public sealed class RunsController(CQPulseDbContext dbContext, ISimulationService simulationService) : ControllerBase
{
    [HttpGet]
    public async Task<PageResponse<RunSummaryDto>> GetAll(
        Guid? projectId,
        Guid? testSuiteId,
        string? search,
        RunStatus? status,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string sort = "requested-desc",
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.TestRuns.AsNoTracking();
        if (projectId is not null) query = query.Where(x => x.ProjectId == projectId);
        if (testSuiteId is not null) query = query.Where(x => x.TestSuiteId == testSuiteId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.RunLabel.Contains(search));
        if (status is not null) query = query.Where(x => x.Status == status);
        if (fromUtc is not null) query = query.Where(x => x.RequestedAtUtc >= fromUtc);
        if (toUtc is not null) query = query.Where(x => x.RequestedAtUtc < toUtc); // exclusive upper bound
        var total = await query.CountAsync(cancellationToken);
        var ordered = sort.ToLowerInvariant() switch
        {
            "requested-asc" => query.OrderBy(x => x.RequestedAtUtc),
            "completed-desc" => query.OrderByDescending(x => x.CompletedAtUtc),
            "completed-asc" => query.OrderBy(x => x.CompletedAtUtc),
            _ => query.OrderByDescending(x => x.RequestedAtUtc)
        };
        var items = await ordered
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(RunQueries.Summary)
            .ToListAsync(cancellationToken);
        return new PageResponse<RunSummaryDto>(items, total, normalizedPage, normalizedSize);
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<RunDetailDto>> Get(Guid runId, CancellationToken cancellationToken) =>
        await RunQueries.GetDetailAsync(dbContext, runId, cancellationToken) is { } run ? Ok(run) : NotFound();

    /// <summary>Queues a new run with the original configuration and seed, linked to the original run.</summary>
    [HttpPost("{runId:guid}/rerun")]
    public async Task<ActionResult<QueuedRunResponse>> Rerun(Guid runId, CancellationToken cancellationToken)
    {
        var newRunId = await simulationService.RerunAsync(runId, User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        return AcceptedAtAction(nameof(Get), new { runId = newRunId }, new QueuedRunResponse(newRunId));
    }

    [HttpGet("{runId:guid}/results")]
    public async Task<PageResponse<RunResultDto>> GetResults(
        Guid runId, TestOutcome? outcome, string? signature, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.TestCaseResults.AsNoTracking().Where(x => x.TestRunId == runId);
        if (outcome is not null) query = query.Where(x => x.Outcome == outcome);
        if (!string.IsNullOrWhiteSpace(signature))
        {
            // Signatures are computed from (category, normalized message), so resolve them to the run's raw message variants.
            var variants = await dbContext.TestCaseResults.AsNoTracking()
                .Where(x => x.TestRunId == runId && x.Outcome == TestOutcome.Failed)
                .Select(x => new { x.FailureCategory, x.ErrorMessage }).Distinct().ToListAsync(cancellationToken);
            var matches = variants.Where(v => FailureSignature.Compute(v.FailureCategory, v.ErrorMessage) == signature.Trim()).ToList();
            if (matches.Count == 0) return new PageResponse<RunResultDto>([], 0, normalizedPage, normalizedSize);
            var category = matches[0].FailureCategory;
            var messages = matches.Select(m => m.ErrorMessage).ToList();
            query = query.Where(x => x.Outcome == TestOutcome.Failed && x.FailureCategory == category && messages.Contains(x.ErrorMessage));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CompletedAtUtc).ThenBy(x => x.Id)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => new RunResultDto(
                x.Id, x.TestCaseNameSnapshot, x.Outcome, x.DurationMs, x.StartedAtUtc, x.CompletedAtUtc,
                x.ErrorMessage, x.FailureCategory))
            .ToListAsync(cancellationToken);
        return new PageResponse<RunResultDto>(items, total, normalizedPage, normalizedSize);
    }

    [HttpGet("{runId:guid}/logs")]
    public async Task<PageResponse<RunLogDto>> GetLogs(Guid runId, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.ExecutionLogs.AsNoTracking().Where(x => x.TestRunId == runId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.TimestampUtc).ThenBy(x => x.Id)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => new RunLogDto(x.Id, x.TimestampUtc, x.Level, x.Message, x.MetadataJson))
            .ToListAsync(cancellationToken);
        return new PageResponse<RunLogDto>(items, total, normalizedPage, normalizedSize);
    }
}
