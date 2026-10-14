using CQPulse.Application.Catalog;
using CQPulse.Application.Common;
using CQPulse.Domain.Entities;
using CQPulse.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CQPulse.Infrastructure.Catalog;

internal sealed class CatalogService(CQPulseDbContext dbContext) : ICatalogService
{
    public async Task<PageResponse<ProjectDto>> GetProjectsAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Projects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ProjectDto(x.Id, x.Name, x.Description, x.Status, x.CreatedAtUtc)).ToListAsync(cancellationToken);
        return new PageResponse<ProjectDto>(items, total, page, pageSize);
    }

    public Task<ProjectDto?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken) =>
        dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new ProjectDto(x.Id, x.Name, x.Description, x.Status, x.CreatedAtUtc)).SingleOrDefaultAsync(cancellationToken);

    public async Task<ProjectDto> CreateProjectAsync(SaveProjectRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Description, MaxDescriptionLength);
        var project = new Project { Name = request.Name.Trim(), Description = request.Description?.Trim(), Status = request.Status };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectDto(project.Id, project.Name, project.Description, project.Status, project.CreatedAtUtc);
    }

    public async Task<ProjectDto?> UpdateProjectAsync(Guid projectId, SaveProjectRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Description, MaxDescriptionLength);
        var project = await dbContext.Projects.SingleOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project is null) return null;
        project.Name = request.Name.Trim(); project.Description = request.Description?.Trim();
        project.Status = request.Status; project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectDto(project.Id, project.Name, project.Description, project.Status, project.CreatedAtUtc);
    }

    public async Task<PageResponse<TestSuiteDto>> GetSuitesAsync(Guid projectId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.TestSuites.AsNoTracking().Where(x => x.ProjectId == projectId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TestSuiteDto(x.Id, x.ProjectId, x.Name, x.Description, x.TestCases.Count)).ToListAsync(cancellationToken);
        return new PageResponse<TestSuiteDto>(items, total, page, pageSize);
    }

    public Task<TestSuiteDto?> GetSuiteAsync(Guid suiteId, CancellationToken cancellationToken) =>
        dbContext.TestSuites.AsNoTracking().Where(x => x.Id == suiteId)
            .Select(x => new TestSuiteDto(x.Id, x.ProjectId, x.Name, x.Description, x.TestCases.Count)).SingleOrDefaultAsync(cancellationToken);

    public async Task<TestSuiteDto?> CreateSuiteAsync(Guid projectId, SaveTestSuiteRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Description, MaxDescriptionLength);
        if (!await dbContext.Projects.AnyAsync(x => x.Id == projectId, cancellationToken)) return null;
        await EnsureUniqueSuiteNameAsync(projectId, request.Name, null, cancellationToken);
        var suite = new TestSuite { ProjectId = projectId, Name = request.Name.Trim(), Description = request.Description?.Trim() };
        dbContext.TestSuites.Add(suite);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TestSuiteDto(suite.Id, suite.ProjectId, suite.Name, suite.Description, 0);
    }

    public async Task<TestSuiteDto?> UpdateSuiteAsync(Guid suiteId, SaveTestSuiteRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Description, MaxDescriptionLength);
        var suite = await dbContext.TestSuites.Include(x => x.TestCases).SingleOrDefaultAsync(x => x.Id == suiteId, cancellationToken);
        if (suite is null) return null;
        await EnsureUniqueSuiteNameAsync(suite.ProjectId, request.Name, suite.Id, cancellationToken);
        suite.Name = request.Name.Trim(); suite.Description = request.Description?.Trim(); suite.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new TestSuiteDto(suite.Id, suite.ProjectId, suite.Name, suite.Description, suite.TestCases.Count);
    }

    public async Task<PageResponse<TestCaseDto>> GetCasesAsync(Guid suiteId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.TestCases.AsNoTracking().Where(x => x.TestSuiteId == suiteId);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TestCaseDto(x.Id, x.TestSuiteId, x.Name, x.Description, x.Enabled, x.Priority, x.SimulatedOutcome, x.SimulatedDurationMs))
            .ToListAsync(cancellationToken);
        return new PageResponse<TestCaseDto>(items, total, page, pageSize);
    }

    public Task<TestCaseDto?> GetCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        dbContext.TestCases.AsNoTracking().Where(x => x.Id == caseId)
            .Select(x => new TestCaseDto(x.Id, x.TestSuiteId, x.Name, x.Description, x.Enabled, x.Priority, x.SimulatedOutcome, x.SimulatedDurationMs))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<TestCaseDto?> CreateCaseAsync(Guid suiteId, SaveTestCaseRequest request, CancellationToken cancellationToken)
    {
        ValidateCase(request);
        if (!await dbContext.TestSuites.AnyAsync(x => x.Id == suiteId, cancellationToken)) return null;
        var testCase = new TestCase { TestSuiteId = suiteId, Name = request.Name.Trim(), Description = request.Description?.Trim(), Enabled = request.Enabled, Priority = request.Priority, SimulatedOutcome = request.SimulatedOutcome, SimulatedDurationMs = request.SimulatedDurationMs };
        dbContext.TestCases.Add(testCase);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(testCase);
    }

    public async Task<TestCaseDto?> UpdateCaseAsync(Guid caseId, SaveTestCaseRequest request, CancellationToken cancellationToken)
    {
        ValidateCase(request);
        var testCase = await dbContext.TestCases.SingleOrDefaultAsync(x => x.Id == caseId, cancellationToken);
        if (testCase is null) return null;
        testCase.Name = request.Name.Trim(); testCase.Description = request.Description?.Trim(); testCase.Enabled = request.Enabled;
        testCase.Priority = request.Priority; testCase.SimulatedOutcome = request.SimulatedOutcome; testCase.SimulatedDurationMs = request.SimulatedDurationMs; testCase.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(testCase);
    }

    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2_000;
    private const int MaxCaseDescriptionLength = 4_000;
    private const int MaxSimulatedDurationMs = 60_000;

    private async Task EnsureUniqueSuiteNameAsync(Guid projectId, string name, Guid? excludingSuiteId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (await dbContext.TestSuites.AnyAsync(x => x.ProjectId == projectId && x.Name == trimmed && x.Id != excludingSuiteId, cancellationToken))
            throw new ConflictException($"A suite named '{trimmed}' already exists in this project.");
    }

    private static ValidationErrors NameAndDescriptionErrors(string? name, string? description, int maxDescriptionLength) =>
        new ValidationErrors()
            .AddIf(string.IsNullOrWhiteSpace(name), "Name", "Name is required.")
            .AddIf(name?.Trim().Length > MaxNameLength, "Name", $"Name must not exceed {MaxNameLength} characters.")
            .AddIf(description?.Trim().Length > maxDescriptionLength, "Description", $"Description must not exceed {maxDescriptionLength:N0} characters.");

    private static void Validate(string? name, string? description, int maxDescriptionLength) =>
        NameAndDescriptionErrors(name, description, maxDescriptionLength).ThrowIfAny();

    private static void ValidateCase(SaveTestCaseRequest request) =>
        NameAndDescriptionErrors(request.Name, request.Description, MaxCaseDescriptionLength)
            .AddIf(request.SimulatedDurationMs is < 0 or > MaxSimulatedDurationMs, nameof(request.SimulatedDurationMs),
                $"Simulated duration must be between 0 and {MaxSimulatedDurationMs:N0} ms.")
            .AddIf(!Enum.IsDefined(request.Priority), nameof(request.Priority), "Unknown priority.")
            .AddIf(!Enum.IsDefined(request.SimulatedOutcome), nameof(request.SimulatedOutcome), "Unknown simulated outcome.")
            .ThrowIfAny();

    private static TestCaseDto ToDto(TestCase testCase) => new(testCase.Id, testCase.TestSuiteId, testCase.Name, testCase.Description, testCase.Enabled, testCase.Priority, testCase.SimulatedOutcome, testCase.SimulatedDurationMs);
}
