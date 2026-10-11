using CQPulse.Application.Catalog;
using Microsoft.AspNetCore.Mvc;

namespace CQPulse.Api.Controllers;

[ApiController]
public sealed class TestCasesController(ICatalogService catalog) : ControllerBase
{
    [HttpGet("api/suites/{suiteId:guid}/cases")]
    public Task<PageResponse<TestCaseDto>> GetForSuite(Guid suiteId, string? search, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.GetCasesAsync(suiteId, search, ProjectsController.NormalizePage(page), ProjectsController.NormalizePageSize(pageSize), cancellationToken);

    [HttpPost("api/suites/{suiteId:guid}/cases")]
    public async Task<ActionResult<TestCaseDto>> Create(Guid suiteId, SaveTestCaseRequest request, CancellationToken cancellationToken) =>
        (await catalog.CreateCaseAsync(suiteId, request, cancellationToken)) is { } testCase
            ? CreatedAtAction(nameof(Get), new { caseId = testCase.Id }, testCase) : NotFound();

    [HttpGet("api/test-cases/{caseId:guid}")]
    public async Task<ActionResult<TestCaseDto>> Get(Guid caseId, CancellationToken cancellationToken) =>
        (await catalog.GetCaseAsync(caseId, cancellationToken)) is { } testCase ? Ok(testCase) : NotFound();

    [HttpPut("api/test-cases/{caseId:guid}")]
    public async Task<ActionResult<TestCaseDto>> Update(Guid caseId, SaveTestCaseRequest request, CancellationToken cancellationToken) =>
        (await catalog.UpdateCaseAsync(caseId, request, cancellationToken)) is { } testCase ? Ok(testCase) : NotFound();
}
