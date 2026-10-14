using CQPulse.Application.Catalog;
using CQPulse.Application.Runs;
using CQPulse.Domain;
using CQPulse.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Api.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
public sealed class TestCasesController(ICatalogService catalog, CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet("api/suites/{suiteId:guid}/cases")]
    public Task<PageResponse<TestCaseDto>> GetForSuite(
        Guid suiteId, string? search, bool? enabled, TestPriority? priority, string? sort,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.GetCasesAsync(suiteId,
            new TestCaseQuery(search, enabled, priority, sort, ProjectsController.NormalizePage(page), ProjectsController.NormalizePageSize(pageSize)),
            cancellationToken);

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

    /// <summary>Enables or disables a case for future catalog runs; past results are unaffected.</summary>
    [HttpPatch("api/test-cases/{caseId:guid}/enabled")]
    public async Task<ActionResult<TestCaseDto>> SetEnabled(Guid caseId, SetEnabledRequest request, CancellationToken cancellationToken) =>
        (await catalog.SetCaseEnabledAsync(caseId, request.Enabled, cancellationToken)) is { } testCase ? Ok(testCase) : NotFound();

    /// <summary>Deletes a case. Historical results remain, identified by their name snapshot.</summary>
    [HttpDelete("api/test-cases/{caseId:guid}")]
    public async Task<IActionResult> Delete(Guid caseId, CancellationToken cancellationToken) =>
        await catalog.DeleteCaseAsync(caseId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Paged execution history of a case across runs, newest first.</summary>
    [HttpGet("api/test-cases/{caseId:guid}/results")]
    public async Task<ActionResult<PageResponse<TestCaseExecutionDto>>> GetHistory(
        Guid caseId, TestOutcome? outcome, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.TestCases.AnyAsync(x => x.Id == caseId, cancellationToken)) return NotFound();
        var normalizedPage = ProjectsController.NormalizePage(page);
        var normalizedSize = ProjectsController.NormalizePageSize(pageSize);
        var query = dbContext.TestCaseResults.AsNoTracking().Where(x => x.TestCaseId == caseId);
        if (outcome is not null) query = query.Where(x => x.Outcome == outcome);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CompletedAtUtc).ThenBy(x => x.Id)
            .Skip((normalizedPage - 1) * normalizedSize).Take(normalizedSize)
            .Select(x => new TestCaseExecutionDto(
                x.Id, x.TestRunId, x.TestRun.RunLabel, x.TestCaseNameSnapshot, x.Outcome, x.DurationMs,
                x.CompletedAtUtc, x.ErrorMessage, x.FailureCategory))
            .ToListAsync(cancellationToken);
        return new PageResponse<TestCaseExecutionDto>(items, total, normalizedPage, normalizedSize);
    }
}
