using CQPulse.Domain;

namespace CQPulse.Application.Runs;

public sealed record RunSummaryDto(
    Guid Id, Guid ProjectId, Guid? TestSuiteId, string RunLabel, RunStatus Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    int TotalTests, int CompletedTests, int PassedCount, int FailedCount, int SkippedCount, string? ErrorSummary,
    Guid? RerunOfRunId);

public sealed record RunDetailDto(
    Guid Id, Guid ProjectId, Guid? TestSuiteId, string RunLabel, RunStatus Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    int TotalTests, int CompletedTests, int PassedCount, int FailedCount, int SkippedCount,
    string ConfigurationSnapshot, int? RandomSeed, string? ErrorSummary, string? CancellationReason, int? DurationMs,
    Guid? RerunOfRunId, IReadOnlyList<Guid> RerunIds);

public sealed record RunResultDto(
    Guid Id, string TestCaseNameSnapshot, TestOutcome Outcome, int DurationMs,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, string? ErrorMessage, FailureCategory? FailureCategory);

public sealed record RunLogDto(Guid Id, DateTimeOffset TimestampUtc, LogLevel Level, string Message, string? MetadataJson);

public sealed record DashboardSummaryDto(
    int ProjectCount, int TestSuiteCount, int TestCaseCount, int TotalRuns,
    int Passed, int Failed, int Skipped, int ActiveRuns, double PassRate);

/// <summary>One execution of a catalog test case, for its history view.</summary>
public sealed record TestCaseExecutionDto(
    Guid ResultId, Guid RunId, string RunLabel, string TestCaseNameSnapshot, TestOutcome Outcome, int DurationMs,
    DateTimeOffset CompletedAtUtc, string? ErrorMessage, FailureCategory? FailureCategory);

/// <summary>Project detail with persisted counts. Result totals count executions across all of the project's runs.</summary>
public sealed record ProjectSummaryDto(
    Guid Id, string Name, string? Description, ProjectStatus Status, DateTimeOffset CreatedAtUtc,
    int SuiteCount, int TestCaseCount, int EnabledTestCaseCount, int RunCount, int ActiveRunCount,
    int PassedResults, int FailedResults, int SkippedResults, double PassRate,
    IReadOnlyList<RunSummaryDto> RecentRuns);
