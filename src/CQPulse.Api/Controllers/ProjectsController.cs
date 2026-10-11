using CQPulse.Application.Catalog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/projects")]
public sealed class ProjectsController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    public Task<PageResponse<ProjectDto>> GetAll(string? search, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.GetProjectsAsync(search, NormalizePage(page), NormalizePageSize(pageSize), cancellationToken);

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult<ProjectDto>> Get(Guid projectId, CancellationToken cancellationToken) =>
        (await catalog.GetProjectAsync(projectId, cancellationToken)) is { } project ? Ok(project) : NotFound();

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(SaveProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await catalog.CreateProjectAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { projectId = project.Id }, project);
    }

    [HttpPut("{projectId:guid}")]
    public async Task<ActionResult<ProjectDto>> Update(Guid projectId, SaveProjectRequest request, CancellationToken cancellationToken) =>
        (await catalog.UpdateProjectAsync(projectId, request, cancellationToken)) is { } project ? Ok(project) : NotFound();

    internal static int NormalizePage(int page) => Math.Max(1, page);
    internal static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 100);
}
