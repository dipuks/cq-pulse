using System.Security.Claims;
using CQPulse.Application.Decisions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

/// <summary>
/// Decision support for a run's failures, ranked by SmartDog from persisted evidence. Every request is recorded, including
/// when SmartDog is unavailable (status Unavailable) or rejects the request (status Failed). Never changes the run.
/// </summary>
[ApiController]
[Authorize(Roles = "Administrator")]
public sealed class DecisionSupportController(IDecisionSupportService decisions) : ControllerBase
{
    [HttpPost("api/runs/{runId:guid}/decision-support")]
    public Task<DecisionEvaluationDto> Evaluate(Guid runId, [FromBody] DecisionSupportRequest? request, CancellationToken cancellationToken) =>
        decisions.EvaluateRunAsync(runId, request, User.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);

    /// <summary>The run's evaluation history, newest first (up to 50).</summary>
    [HttpGet("api/runs/{runId:guid}/decision-support")]
    public Task<IReadOnlyList<DecisionEvaluationDto>> History(Guid runId, CancellationToken cancellationToken) =>
        decisions.GetForRunAsync(runId, cancellationToken);

    [HttpGet("api/decision-evaluations/{evaluationId:guid}")]
    public async Task<ActionResult<DecisionEvaluationDto>> Get(Guid evaluationId, CancellationToken cancellationToken) =>
        await decisions.GetAsync(evaluationId, cancellationToken) is { } evaluation ? Ok(evaluation) : NotFound();
}
