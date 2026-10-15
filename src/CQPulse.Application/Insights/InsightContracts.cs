using CQPulse.Domain;

namespace CQPulse.Application.Insights;

/// <summary>The analysed window and the equally long window immediately before it (UTC, half-open).</summary>
public sealed record InsightWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset PreviousFromUtc);

/// <summary>
/// A recurring failure: category + normalized message (see <c>FailureSignature</c>), grouped in SQL by the signature stored
/// on each failed result. Occurrences, previous occurrences, affected runs and affected catalog test cases are exact
/// (distinct) counts over every signed failure in the window. <see cref="SampleMessage"/> is a raw message from the
/// signature's most recent failure.
/// </summary>
public sealed record FailureSignatureDto(
    string Signature, FailureCategory? Category, string NormalizedMessage, string SampleMessage,
    int Occurrences, int PreviousOccurrences, int AffectedRuns, int AffectedTestCases,
    DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

/// <param name="TotalFailures">Every failed result in the window (exact).</param>
/// <param name="DistinctSignatures">Distinct signatures among the signed failures in the window (exact).</param>
/// <param name="UnsignedFailures">
/// Failed results in the window that have no stored signature yet (written before the column existed; the background
/// backfill assigns them). They are counted in <paramref name="TotalFailures"/> but in no signature.
/// </param>
/// <param name="PreviousUnsignedFailures">The same for the previous window, which <c>PreviousOccurrences</c> come from.</param>
/// <param name="CountsComplete">True when no failure in either window is waiting for the backfill, so every count is final.</param>
public sealed record FailureSignatureReportDto(
    InsightWindow Window, int TotalFailures, IReadOnlyList<FailureSignatureDto> Signatures,
    int DistinctSignatures, int UnsignedFailures, int PreviousUnsignedFailures, bool CountsComplete);

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
