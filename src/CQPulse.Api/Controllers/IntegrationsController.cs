using CQPulse.Application.Analysis;
using CQPulse.Infrastructure.Integrations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

/// <summary>
/// Health of optional external services. Kept separate from CQ Pulse's own /health/ready so an outage of an optional
/// dependency degrades features instead of taking CQ Pulse down.
/// </summary>
[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/integrations")]
public sealed class IntegrationsController(IIntegrationStatusService status) : ControllerBase
{
    [HttpGet("status")]
    public Task<IReadOnlyList<IntegrationStatusDto>> GetStatus(CancellationToken cancellationToken) => status.CheckAsync(cancellationToken);
}
