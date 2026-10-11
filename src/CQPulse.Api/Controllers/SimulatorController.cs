using CQPulse.Application.Simulation;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/simulator")]
public sealed class SimulatorController(
    ISimulationService simulationService,
    CQPulseDbContext dbContext) : ControllerBase
{
    [HttpPost("preview")]
    public ActionResult<SimulationPreview> Preview(SimulationRequest request) =>
        Ok(simulationService.Preview(request));

    [HttpPost("runs")]
    public async Task<ActionResult<object>> Start(SimulationRequest request, CancellationToken cancellationToken)
    {
        var runId = await simulationService.QueueAsync(request, User.Identity?.Name, cancellationToken);
        return Accepted(new { runId });
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken cancellationToken)
    {
        await simulationService.CancelAsync(runId, cancellationToken);
        return NoContent();
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<object>> GetRun(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.TestRuns.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }
}
