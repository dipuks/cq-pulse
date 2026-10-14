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
[Route("api/projects")]
public sealed class ProjectsController(ICatalogService catalog, CQPulseDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public Task<PageResponse<ProjectDto>> GetAll(
        string? search, ProjectStatus? status, string? sort, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default) =>
        catalog.GetProjectsAsync(new ProjectQuery(search, status, sort, NormalizePage(page), NormalizePageSize(pageSize)), cancellationToken);

    [HttpGet("{projectId:guid}")]
    public async Task<ActionResult<ProjectDto>> Get(Guid projectId, CancellationToken cancellationToken) =>
        (await catalog.GetProjectAsync(projectId, cancellationToken)) is { } project ? Ok(project) : NotFound();

    /// <summary>Project detail with catalog counts, run counts, result totals and the five most recent runs.</summary>
    [HttpGet("{projectId:guid}/summary")]
    public async Task<ActionResult<ProjectSummaryDto>> GetSummary(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.Status, x.CreatedAtUtc,
                SuiteCount = x.TestSuites.Count,
                TestCaseCount = x.TestSuites.SelectMany(s => s.TestCases).Count(),
                EnabledTestCaseCount = x.TestSuites.SelectMany(s => s.TestCases).Count(c => c.Enabled),
                RunCount = x.TestRuns.Count,
                ActiveRunCount = x.TestRuns.Count(r => r.Status == RunStatus.Queued || r.Status == RunStatus.Running),
                Passed = x.TestRuns.Sum(r => r.PassedCount),
                Failed = x.TestRuns.Sum(r => r.FailedCount),
                Skipped = x.TestRuns.Sum(r => r.SkippedCount)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null) return NotFound();

        var recentRuns = await dbContext.TestRuns.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.RequestedAtUtc).Take(5).Select(RunQueries.Summary).ToListAsync(cancellationToken);
        return new ProjectSummaryDto(
            project.Id, project.Name, project.Description, project.Status, project.CreatedAtUtc,
            project.SuiteCount, project.TestCaseCount, project.EnabledTestCaseCount, project.RunCount, project.ActiveRunCount,
            project.Passed, project.Failed, project.Skipped,
            DashboardController.Percentage(project.Passed, project.Passed + project.Failed + project.Skipped),
            recentRuns);
    }

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
