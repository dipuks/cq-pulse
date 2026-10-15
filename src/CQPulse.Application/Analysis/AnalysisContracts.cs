using CQPulse.Domain;

namespace CQPulse.Application.Analysis;

public sealed record AnalysisFindingDto(
    int LineNumber, int EndLineNumber, string? Timestamp, string Message, string Severity, string Category, string Evidence, string Suggestion);

/// <summary>A persisted external analysis. Severity, signal score and findings come from the provider, unchanged.</summary>
/// <param name="WaitingForQueueSlot">
/// Pending but not in the in-memory queue yet, because the queue was full (or the application just started). It stays
/// Pending in the database and is queued automatically, oldest first, as earlier analyses finish.
/// </param>
public sealed record FailureAnalysisDto(
    Guid Id, Guid TestRunId, string Provider, AnalysisStatus Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    int Attempts, string? LastError, int InputFailureCount, bool InputTruncated, string CorrelationId,
    string? ExternalAnalysisId, string? Severity, string? Summary, double? SignalScore, string? TopCategory,
    IReadOnlyList<AnalysisFindingDto> Findings, IReadOnlyList<string> Recommendations, bool WaitingForQueueSlot = false);

public sealed record IntegrationStatusDto(
    string Name, bool Configured, string Status, string? Detail, string? BaseUrl, DateTimeOffset CheckedAtUtc);

public interface IFailureAnalysisService
{
    /// <summary>
    /// Requests analysis of a run's persisted failures. Returns the existing record when an analysis for identical input
    /// is pending, running or succeeded; otherwise creates and queues a new one.
    /// </summary>
    Task<FailureAnalysisDto> RequestAsync(Guid runId, string? userId, CancellationToken cancellationToken);

    /// <summary>Re-queues a finished analysis with freshly built input; never re-executes the run.</summary>
    Task<FailureAnalysisDto> RetryAsync(Guid analysisId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FailureAnalysisDto>> GetForRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<FailureAnalysisDto?> GetAsync(Guid analysisId, CancellationToken cancellationToken);
}
