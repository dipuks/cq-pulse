namespace CQPulse.Domain.Entities;

/// <summary>
/// A decision-support evaluation of a run's failure signatures by an external service (SmartDog). Stores exactly what
/// was submitted (criteria, options and their evidence) and what came back, so every recommendation stays traceable.
/// It never changes the run.
/// </summary>
public sealed class DecisionEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestRunId { get; set; }
    public Guid ProjectId { get; set; }
    public required string Provider { get; set; }
    public AnalysisStatus Status { get; private set; } = AnalysisStatus.Running;
    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public required string CorrelationId { get; set; }
    public string? RequestedByUserId { get; set; }
    public required string Question { get; set; }
    /// <summary>The evaluation request as sent (criteria, options with values, constraints).</summary>
    public required string RequestJson { get; set; }
    /// <summary>Per-option evidence (signature, category, message, counts) behind each submitted value.</summary>
    public required string EvidenceJson { get; set; }
    /// <summary>Notes on the evidence used or omitted (e.g. why triage severity was not considered).</summary>
    public required string EvidenceNotesJson { get; set; }
    public int OptionCount { get; set; }
    /// <summary>The LogTriageAssistant analysis whose severities were used, if any.</summary>
    public Guid? FailureAnalysisId { get; set; }
    public string? ExternalEvaluationId { get; private set; }
    /// <summary>The provider's response as returned.</summary>
    public string? ResultJson { get; private set; }
    public string? LastError { get; private set; }
    public TestRun TestRun { get; set; } = null!;

    public void Succeed(DateTimeOffset now, string? externalId, string resultJson)
    {
        if (Status != AnalysisStatus.Running) throw new InvalidOperationException("Only running evaluations can succeed.");
        Status = AnalysisStatus.Succeeded;
        CompletedAtUtc = now;
        ExternalEvaluationId = externalId;
        ResultJson = resultJson;
    }

    /// <param name="status"><see cref="AnalysisStatus.Failed"/> (rejected or malformed) or <see cref="AnalysisStatus.Unavailable"/> (not configured, down, timeout).</param>
    public void Fail(DateTimeOffset now, AnalysisStatus status, string error)
    {
        if (status is not (AnalysisStatus.Failed or AnalysisStatus.Unavailable)) throw new ArgumentOutOfRangeException(nameof(status));
        if (Status != AnalysisStatus.Running) throw new InvalidOperationException("Only running evaluations can fail.");
        Status = status;
        CompletedAtUtc = now;
        LastError = error;
    }
}
