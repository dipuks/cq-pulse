using System.Security.Claims;
using CQPulse.Application.Analysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

/// <summary>External diagnostic analyses of runs' persisted failures. Never changes runs or results.</summary>
[ApiController]
[Authorize(Roles = "Administrator")]
public sealed class FailureAnalysesController(IFailureAnalysisService analyses) : ControllerBase
{
    /// <summary>Requests analysis; returns the existing record (200) for unchanged input, otherwise 202 with a new one.</summary>
    [HttpPost("api/runs/{runId:guid}/failure-analyses")]
    public async Task<ActionResult<FailureAnalysisDto>> RequestAnalysis(Guid runId, CancellationToken cancellationToken)
    {
        var before = (await analyses.GetForRunAsync(runId, cancellationToken)).Select(x => x.Id).ToHashSet();
        var analysis = await analyses.RequestAsync(runId, User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        return before.Contains(analysis.Id) ? Ok(analysis) : AcceptedAtAction(nameof(Get), new { analysisId = analysis.Id }, analysis);
    }

    [HttpGet("api/runs/{runId:guid}/failure-analyses")]
    public Task<IReadOnlyList<FailureAnalysisDto>> GetForRun(Guid runId, CancellationToken cancellationToken) =>
        analyses.GetForRunAsync(runId, cancellationToken);

    [HttpGet("api/failure-analyses/{analysisId:guid}")]
    public async Task<ActionResult<FailureAnalysisDto>> Get(Guid analysisId, CancellationToken cancellationToken) =>
        await analyses.GetAsync(analysisId, cancellationToken) is { } analysis ? Ok(analysis) : NotFound();

    /// <summary>Re-submits the run's stored failures; the original workload is not re-executed.</summary>
    [HttpPost("api/failure-analyses/{analysisId:guid}/retry")]
    public async Task<ActionResult<FailureAnalysisDto>> Retry(Guid analysisId, CancellationToken cancellationToken)
    {
        var analysis = await analyses.RetryAsync(analysisId, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { analysisId = analysis.Id }, analysis);
    }
}
