using CQPulse.Domain;

namespace CQPulse.Application.Catalog;

public sealed record PageResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
public sealed record ProjectDto(Guid Id, string Name, string? Description, ProjectStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record TestSuiteDto(Guid Id, Guid ProjectId, string Name, string? Description, int TestCaseCount, int EnabledTestCaseCount);
public sealed record TestCaseDto(
    Guid Id, Guid TestSuiteId, string Name, string? Description, bool Enabled,
    TestPriority Priority, SimulatedOutcome SimulatedOutcome, int SimulatedDurationMs);
public sealed record SaveProjectRequest(string Name, string? Description, ProjectStatus Status = ProjectStatus.Active);
public sealed record SaveTestSuiteRequest(string Name, string? Description);
public sealed record SaveTestCaseRequest(
    string Name, string? Description, bool Enabled, TestPriority Priority,
    SimulatedOutcome SimulatedOutcome, int SimulatedDurationMs);

public interface ICatalogService
{
    Task<PageResponse<ProjectDto>> GetProjectsAsync(ProjectQuery query, CancellationToken cancellationToken);
    Task<ProjectDto?> GetProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<ProjectDto> CreateProjectAsync(SaveProjectRequest request, CancellationToken cancellationToken);
    Task<ProjectDto?> UpdateProjectAsync(Guid projectId, SaveProjectRequest request, CancellationToken cancellationToken);
    Task<PageResponse<TestSuiteDto>> GetSuitesAsync(Guid projectId, int page, int pageSize, CancellationToken cancellationToken);
    Task<TestSuiteDto?> GetSuiteAsync(Guid suiteId, CancellationToken cancellationToken);
    Task<TestSuiteDto?> CreateSuiteAsync(Guid projectId, SaveTestSuiteRequest request, CancellationToken cancellationToken);
    Task<TestSuiteDto?> UpdateSuiteAsync(Guid suiteId, SaveTestSuiteRequest request, CancellationToken cancellationToken);
    Task<PageResponse<TestCaseDto>> GetCasesAsync(Guid suiteId, TestCaseQuery query, CancellationToken cancellationToken);
    /// <summary>Returns null when the test case does not exist.</summary>
    Task<TestCaseDto?> SetCaseEnabledAsync(Guid caseId, bool enabled, CancellationToken cancellationToken);
    /// <summary>Deletes a test case. Its historical results keep their name snapshot; their link becomes null.</summary>
    Task<bool> DeleteCaseAsync(Guid caseId, CancellationToken cancellationToken);
    Task<TestCaseDto?> GetCaseAsync(Guid caseId, CancellationToken cancellationToken);
    Task<TestCaseDto?> CreateCaseAsync(Guid suiteId, SaveTestCaseRequest request, CancellationToken cancellationToken);
    Task<TestCaseDto?> UpdateCaseAsync(Guid caseId, SaveTestCaseRequest request, CancellationToken cancellationToken);
}

public sealed record ProjectQuery(string? Search, ProjectStatus? Status, string? Sort, int Page, int PageSize);
public sealed record TestCaseQuery(string? Search, bool? Enabled, TestPriority? Priority, string? Sort, int Page, int PageSize);
public sealed record SetEnabledRequest(bool Enabled);
