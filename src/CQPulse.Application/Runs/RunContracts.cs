using CQPulse.Domain;

namespace CQPulse.Application.Runs;

public sealed record RunSummaryDto(
    Guid Id, Guid ProjectId, Guid? TestSuiteId, string RunLabel, RunStatus Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    int TotalTests, int CompletedTests, int PassedCount, int FailedCount, int SkippedCount, string? ErrorSummary);

public sealed record RunDetailDto(
    Guid Id, Guid ProjectId, Guid? TestSuiteId, string RunLabel, RunStatus Status,
    DateTimeOffset RequestedAtUtc, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    int TotalTests, int CompletedTests, int PassedCount, int FailedCount, int SkippedCount,
    string ConfigurationSnapshot, int? RandomSeed, string? ErrorSummary, string? CancellationReason, int? DurationMs);

public sealed record RunResultDto(
    Guid Id, string TestCaseNameSnapshot, TestOutcome Outcome, int DurationMs,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, string? ErrorMessage, FailureCategory? FailureCategory);

public sealed record RunLogDto(Guid Id, DateTimeOffset TimestampUtc, LogLevel Level, string Message, string? MetadataJson);

public sealed record DashboardSummaryDto(
    int ProjectCount, int TestSuiteCount, int TestCaseCount, int TotalRuns,
    int Passed, int Failed, int Skipped, int ActiveRuns, double PassRate);
