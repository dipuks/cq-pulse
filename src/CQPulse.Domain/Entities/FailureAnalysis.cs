namespace CQPulse.Domain.Entities;

/// <summary>
/// An external diagnostic analysis of a run's persisted failures (e.g. by LogTriageAssistant). It references the run but
/// never changes it: analysis outcomes live only here.
/// </summary>
public sealed class FailureAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }
    public required string Provider { get; set; }
    public AnalysisStatus Status { get; private set; } = AnalysisStatus.Pending;
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>SHA-256 of the submitted document; identical input is not analysed twice unless retried explicitly.</summary>
    public required string InputDigest { get; set; }
    public int InputFailureCount { get; set; }
    public bool InputTruncated { get; set; }
    public required string CorrelationId { get; set; }
    public string? RequestedByUserId { get; set; }

    public string? ExternalAnalysisId { get; private set; }
    public string? Severity { get; private set; }
    public string? Summary { get; private set; }
    /// <summary>The provider's heuristic signal score as reported (LogTriageAssistant: 0–100; not a calibrated probability).</summary>
    public double? SignalScore { get; private set; }
    public string? TopCategory { get; private set; }
    public string? FindingsJson { get; private set; }
    public string? RecommendationsJson { get; private set; }
    /// <summary>
    /// Highest provider severity per CQ Pulse failure signature (JSON object signature → severity), mapped from every
    /// finding returned by the provider through the submitted line numbers. Null for analyses created before this existed.
    /// </summary>
    public string? SignatureSeveritiesJson { get; private set; }
    public TestRun TestRun { get; set; } = null!;

    public void Start(DateTimeOffset now)
    {
        if (Status is not (AnalysisStatus.Pending)) throw new InvalidOperationException("Only pending analyses can start.");
        Status = AnalysisStatus.Running;
        StartedAtUtc = now;
        Attempts++;
    }

    public void Succeed(DateTimeOffset now, AnalysisOutcome outcome)
    {
        if (Status != AnalysisStatus.Running) throw new InvalidOperationException("Only running analyses can succeed.");
        Status = AnalysisStatus.Succeeded;
        CompletedAtUtc = now;
        LastError = null;
        ExternalAnalysisId = outcome.ExternalId;
        Severity = outcome.Severity;
        Summary = outcome.Summary;
        SignalScore = outcome.SignalScore;
        TopCategory = outcome.TopCategory;
        FindingsJson = outcome.FindingsJson;
        RecommendationsJson = outcome.RecommendationsJson;
        SignatureSeveritiesJson = outcome.SignatureSeveritiesJson;
    }

    /// <param name="status"><see cref="AnalysisStatus.Failed"/> (bad request/response) or <see cref="AnalysisStatus.Unavailable"/> (service down, timeout).</param>
    public void Fail(DateTimeOffset now, AnalysisStatus status, string error)
    {
        if (status is not (AnalysisStatus.Failed or AnalysisStatus.Unavailable)) throw new ArgumentOutOfRangeException(nameof(status));
        if (Status is not (AnalysisStatus.Pending or AnalysisStatus.Running)) throw new InvalidOperationException("Only pending or running analyses can fail.");
        Status = status;
        CompletedAtUtc = now;
        LastError = error;
    }

    /// <summary>Re-queues a finished analysis. Stored run data is resubmitted; the run itself is never re-executed.</summary>
    public void Retry(string correlationId)
    {
        if (Status is not (AnalysisStatus.Failed or AnalysisStatus.Unavailable or AnalysisStatus.Succeeded))
            throw new InvalidOperationException("Only finished analyses can be retried.");
        Status = AnalysisStatus.Pending;
        CorrelationId = correlationId;
        StartedAtUtc = null;
        CompletedAtUtc = null;
    }
}

public sealed record AnalysisOutcome(
    string? ExternalId, string Severity, string Summary, double? SignalScore, string? TopCategory,
    string FindingsJson, string RecommendationsJson, string? SignatureSeveritiesJson = null);
