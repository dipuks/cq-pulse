using CQPulse.Domain;

namespace CQPulse.Application.Simulation;

public sealed record SimulationRequest(
    Guid ProjectId,
    Guid? TestSuiteId,
    int TestCount,
    int MaxConcurrency,
    int MinimumDurationMs,
    int MaximumDurationMs,
    int PassPercentage,
    int FailurePercentage,
    int SkippedPercentage,
    IReadOnlyList<FailureCategory>? FailureCategories,
    int? RandomSeed,
    string? RunLabel,
    WorkloadSource WorkloadSource = WorkloadSource.Synthetic);

/// <summary>
/// Exactly what will be queued. Outcome counts are exact: the simulator allocates outcomes by percentage
/// (largest remainder), so the run's final totals equal these numbers.
/// </summary>
public sealed record SimulationPreview(
    int TestCount,
    int ExpectedPassed,
    int ExpectedFailed,
    int ExpectedSkipped,
    int MaxConcurrency,
    string ResourceWarning,
    string ProjectName,
    string? TestSuiteName,
    int MinimumDurationMs,
    int MaximumDurationMs,
    IReadOnlyList<FailureCategory> FailureCategories,
    int? RandomSeed,
    string? RunLabel,
    double EstimatedDurationSeconds,
    WorkloadSource WorkloadSource = WorkloadSource.Synthetic,
    int CatalogCaseCount = 0,
    string? WorkloadDescription = null);

/// <summary>Server-enforced limits, exposed so clients validate against the same numbers.</summary>
public sealed record SimulatorLimitsDto(int MaxTestCount, int MaxConcurrency, int MaxDurationMs, int LargeWorkloadThreshold);

public sealed record RunProgress(
    Guid RunId,
    RunStatus Status,
    int TotalTests,
    int CompletedTests,
    int PassedCount,
    int FailedCount,
    int SkippedCount,
    string? CurrentTestName);

public interface ISimulationService
{
    SimulatorLimitsDto GetLimits();
    Task<SimulationPreview> PreviewAsync(SimulationRequest request, CancellationToken cancellationToken);
    Task<Guid> QueueAsync(SimulationRequest request, string? userId, CancellationToken cancellationToken);
    /// <summary>Queues a new run with the same configuration (and seed) as an existing run, linked to it.</summary>
    Task<Guid> RerunAsync(Guid runId, string? userId, CancellationToken cancellationToken);
    Task CancelAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IRunProgressPublisher
{
    Task PublishAsync(RunProgress progress, CancellationToken cancellationToken);
}
