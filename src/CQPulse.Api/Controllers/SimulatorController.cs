using System.Security.Claims;
using CQPulse.Application.Runs;
using CQPulse.Application.Simulation;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/simulator")]
public sealed class SimulatorController(
    ISimulationService simulationService,
    CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet("limits")]
    public SimulatorLimitsDto Limits() => simulationService.GetLimits();

    [HttpPost("preview")]
    public Task<SimulationPreview> Preview(SimulationRequest request, CancellationToken cancellationToken) =>
        simulationService.PreviewAsync(request, cancellationToken);

    [HttpPost("runs")]
    public async Task<ActionResult<QueuedRunResponse>> Start(SimulationRequest request, CancellationToken cancellationToken)
    {
        var runId = await simulationService.QueueAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        return AcceptedAtAction(nameof(GetRun), new { runId }, new QueuedRunResponse(runId));
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken cancellationToken)
    {
        await simulationService.CancelAsync(runId, cancellationToken);
        return NoContent();
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<RunDetailDto>> GetRun(Guid runId, CancellationToken cancellationToken) =>
        await RunQueries.GetDetailAsync(dbContext, runId, cancellationToken) is { } run ? Ok(run) : NotFound();
}

public sealed record QueuedRunResponse(Guid RunId);
