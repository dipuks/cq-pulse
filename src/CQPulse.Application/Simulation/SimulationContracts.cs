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
    int? RandomSeed,
    string? RunLabel);

public sealed record SimulationPreview(
    int TestCount,
    int ExpectedPassed,
    int ExpectedFailed,
    int ExpectedSkipped,
    int MaxConcurrency,
    string ResourceWarning);

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
    SimulationPreview Preview(SimulationRequest request);
    Task<Guid> QueueAsync(SimulationRequest request, string? userId, CancellationToken cancellationToken);
    Task CancelAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IRunProgressPublisher
{
    Task PublishAsync(RunProgress progress, CancellationToken cancellationToken);
}
