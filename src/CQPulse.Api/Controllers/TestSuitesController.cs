using CQPulse.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

[ApiController]
public sealed class TestSuitesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet("api/projects/{projectId:guid}/suites")]
    public Task<PageResponse<TestSuiteDto>> GetForProject(Guid projectId, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.GetSuitesAsync(projectId, ProjectsController.NormalizePage(page), ProjectsController.NormalizePageSize(pageSize), cancellationToken);

    [HttpPost("api/projects/{projectId:guid}/suites")]
    public async Task<ActionResult<TestSuiteDto>> Create(Guid projectId, SaveTestSuiteRequest request, CancellationToken cancellationToken) =>
        (await catalog.CreateSuiteAsync(projectId, request, cancellationToken)) is { } suite
            ? CreatedAtAction(nameof(Get), new { suiteId = suite.Id }, suite) : NotFound();

    [HttpGet("api/suites/{suiteId:guid}")]
    public async Task<ActionResult<TestSuiteDto>> Get(Guid suiteId, CancellationToken cancellationToken) =>
        (await catalog.GetSuiteAsync(suiteId, cancellationToken)) is { } suite ? Ok(suite) : NotFound();

    [HttpPut("api/suites/{suiteId:guid}")]
    public async Task<ActionResult<TestSuiteDto>> Update(Guid suiteId, SaveTestSuiteRequest request, CancellationToken cancellationToken) =>
        (await catalog.UpdateSuiteAsync(suiteId, request, cancellationToken)) is { } suite ? Ok(suite) : NotFound();
}
