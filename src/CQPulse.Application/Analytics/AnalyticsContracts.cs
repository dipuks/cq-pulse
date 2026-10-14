using CQPulse.Domain;

namespace CQPulse.Application.Analytics;

/// <summary>Aggregates over persisted test-case results (one row per executed test, across all runs).</summary>
public sealed record AnalyticsSummaryDto(
    int Total, int Passed, int Failed, int Skipped, double PassRate, double FailureRate, double? AverageDurationMs);

/// <summary>One UTC calendar day of results, keyed by <c>CompletedAtUtc</c>.</summary>
public sealed record TrendPointDto(DateOnly Date, int Passed, int Failed, int Skipped, double AverageDurationMs);

public sealed record FailureCategoryCountDto(FailureCategory Category, int Count);
