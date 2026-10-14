using CQPulse.Domain;

namespace CQPulse.Application.Decisions;

/// <summary>
/// The evidence criteria CQ Pulse submits for ranking a run's failure signatures. All come from persisted data; nothing
/// is estimated. A criterion whose evidence is unavailable for any option is left out entirely (and the reason recorded)
/// rather than filled with zeros.
/// </summary>
public static class DecisionCriteria
{
    public const string OccurrencesInRun = "occurrencesInRun";
    public const string Recurrence30Days = "recurrence30d";
    public const string AffectedTestCases = "affectedTestCases";
    public const string TriageSeverity = "triageSeverity";

    public static readonly IReadOnlyList<string> All = [OccurrencesInRun, Recurrence30Days, AffectedTestCases, TriageSeverity];

    public static readonly IReadOnlyDictionary<string, double> DefaultWeights = new Dictionary<string, double>
    {
        [OccurrencesInRun] = 0.35, [Recurrence30Days] = 0.25, [AffectedTestCases] = 0.15, [TriageSeverity] = 0.25
    };

    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [OccurrencesInRun] = "Occurrences in this run",
        [Recurrence30Days] = "Project recurrence (30 days)",
        [AffectedTestCases] = "Affected catalog test cases",
        [TriageSeverity] = "Triage severity (LogTriageAssistant)"
    };

    public static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>
    {
        [OccurrencesInRun] = "Failed results in this run with this signature.",
        [Recurrence30Days] = "Failed results with this signature across the project in the last 30 UTC days (this run included when it falls in that window).",
        [AffectedTestCases] = "Distinct catalog test cases that failed with this signature in this run. Synthetic tests have no catalog case, so their count is a true 0.",
        [TriageSeverity] = "Highest LogTriageAssistant finding severity for this signature's lines in the run's latest completed analysis (INFO 1, MEDIUM 2, HIGH 3, CRITICAL 4). Used only when that analysis matches the run's current data and covers every ranked signature."
    };
}

/// <summary>Maps provider severity labels to an ordinal scale. Unknown labels have no rank (they are not treated as low).</summary>
public static class SeverityScale
{
    public static int? Rank(string? severity) => severity?.Trim().ToUpperInvariant() switch
    {
        "INFO" => 1,
        "MEDIUM" => 2,
        "HIGH" => 3,
        "CRITICAL" => 4,
        _ => null
    };

    public static string Label(int rank) => rank switch { 4 => "CRITICAL", 3 => "HIGH", 2 => "MEDIUM", _ => "INFO" };
}

/// <summary>Optional overrides: criterion weights (> 0) and a minimum number of occurrences in the run (hard constraint).</summary>
public sealed record DecisionSupportRequest(IReadOnlyDictionary<string, double>? Weights, int? MinimumOccurrences);

public sealed record DecisionCriterionDto(string Id, string Name, double Weight, string Direction, string Source);

/// <summary>The evidence behind one option's submitted values.</summary>
public sealed record DecisionOptionEvidenceDto(
    string OptionId, string Signature, FailureCategory? Category, string NormalizedMessage, string SampleMessage,
    int OccurrencesInRun, int Recurrence30Days, int AffectedTestCases, string? TriageSeverity);

// SmartDog response contract (POST /api/decisions/evaluate), stored and returned as received.
public sealed record DecisionContributionDto(string CriterionId, double RawValue, double NormalizedValue, double NormalizedWeight, double Contribution);
public sealed record DecisionRankedOptionDto(
    int Rank, string OptionId, string Name, double Score, IReadOnlyList<DecisionContributionDto> Contributions, string Explanation,
    IReadOnlyList<string> TiedWithOptionIds);
public sealed record DecisionInfeasibleOptionDto(string OptionId, string Name, IReadOnlyList<string> Violations);
public sealed record DecisionWeightedCriterionDto(
    string Id, string Name, string Direction, double Weight, double NormalizedWeight, double? MinimumValue, double? MaximumValue);
public sealed record DecisionResultDto(
    string? EvaluationId, string Status, string? Question, string Method, IReadOnlyList<DecisionWeightedCriterionDto> Criteria,
    IReadOnlyList<DecisionRankedOptionDto> Ranking, IReadOnlyList<DecisionInfeasibleOptionDto> Infeasible,
    IReadOnlyList<string> Notes, string Limitations);

/// <summary>A persisted decision-support evaluation: what was submitted, why, and what came back (or why it failed).</summary>
public sealed record DecisionEvaluationDto(
    Guid Id, Guid TestRunId, string Provider, AnalysisStatus Status, DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc,
    string CorrelationId, string Question, IReadOnlyList<DecisionCriterionDto> Criteria, int? MinimumOccurrences,
    IReadOnlyList<DecisionOptionEvidenceDto> Evidence, IReadOnlyList<string> EvidenceNotes, Guid? FailureAnalysisId,
    string? ExternalEvaluationId, DecisionResultDto? Result, string? LastError);

public interface IDecisionSupportService
{
    /// <summary>
    /// Ranks the run's failure signatures with SmartDog using persisted evidence and records the outcome, including
    /// when SmartDog is not configured or unavailable. Never changes the run.
    /// </summary>
    Task<DecisionEvaluationDto> EvaluateRunAsync(Guid runId, DecisionSupportRequest? request, string? userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DecisionEvaluationDto>> GetForRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<DecisionEvaluationDto?> GetAsync(Guid evaluationId, CancellationToken cancellationToken);
}
