using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await dbContext.Projects.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Description, x.Status, x.CreatedAtUtc })
            .ToListAsync(cancellationToken));
}
