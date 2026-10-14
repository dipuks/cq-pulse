using CQPulse.Domain;

namespace CQPulse.Application.Insights;

/// <summary>The analysed window and the equally long window immediately before it (UTC, half-open).</summary>
public sealed record InsightWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset PreviousFromUtc);

/// <summary>
/// A recurring failure: category + normalized message. Occurrence counts are exact. Affected run/test counts are exact
/// for a single message variant; when several variants normalize to the same signature they are summed (an upper bound)
/// and <see cref="CountsAreUpperBounds"/> is true.
/// </summary>
public sealed record FailureSignatureDto(
    string Signature, FailureCategory? Category, string NormalizedMessage, string SampleMessage,
    int Occurrences, int PreviousOccurrences, int AffectedRuns, int AffectedTestCases,
    DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int MessageVariants, bool CountsAreUpperBounds);

public sealed record FailureSignatureReportDto(InsightWindow Window, int TotalFailures, IReadOnlyList<FailureSignatureDto> Signatures);

/// <summary>A test case that both passed and failed within the window, with enough executions to say so.</summary>
public sealed record FlakyCandidateDto(
    Guid TestCaseId, string TestCaseName, Guid TestSuiteId, string SuiteName, SimulatedOutcome ConfiguredOutcome,
    int Executions, int Passed, int Failed, int Skipped, double FailureRate, int OutcomeChanges, bool RandomnessIsConfigured);

public sealed record FlakyReportDto(InsightWindow Window, int MinimumExecutions, int CasesEvaluated, IReadOnlyList<FlakyCandidateDto> Candidates);

public sealed record DailyFailureRateDto(
    DateOnly Date, int Results, int Failed, double FailureRate,
    double? BaselineMean, double? BaselineStdDev, int BaselineDays, double? ZScore, bool IsAnomaly, string Assessment);

/// <summary>Anomaly rule parameters are returned with the results so every flag can be explained.</summary>
public sealed record AnomalyReportDto(
    InsightWindow Window, int BaselineDays, int MinimumBaselineDays, int MinimumDailyResults, double Threshold,
    string Method, IReadOnlyList<DailyFailureRateDto> Days);

public sealed record QualityTrendDto(
    Guid ProjectId, string ProjectName, int Results, int Failed, double FailureRate,
    int PreviousResults, int PreviousFailed, double? PreviousFailureRate, double? ChangePercentagePoints);

public sealed record QualityTrendReportDto(InsightWindow Window, IReadOnlyList<QualityTrendDto> Projects, string PredictionStatus);
