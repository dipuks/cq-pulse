using CQPulse.Domain;

namespace CQPulse.Application.Insights;

/// <summary>Transparent statistical rules used by the insights endpoints. No machine learning is involved.</summary>
public static class InsightCalculator
{
    public const string AnomalyMethod =
        "A day is flagged when its failure rate differs from the mean of the preceding baseline days by more than " +
        "threshold × standard deviation (z-score). Days with too few results, or too few baseline days, are not assessed.";

    /// <summary>Population standard deviation floor, so a perfectly flat baseline does not flag tiny changes.</summary>
    public const double MinimumStdDev = 0.01;

    public sealed record DailyCount(DateOnly Date, int Results, int Failed);

    /// <summary>
    /// Assesses each day in <paramref name="window"/> against the <paramref name="baselineDays"/> calendar days before it
    /// (only days with at least <paramref name="minimumDailyResults"/> results count towards the baseline).
    /// </summary>
    public static IReadOnlyList<DailyFailureRateDto> DetectAnomalies(
        IReadOnlyList<DailyCount> history, IReadOnlyList<DateOnly> window, int baselineDays, int minimumBaselineDays,
        int minimumDailyResults, double threshold)
    {
        var byDate = history.ToDictionary(x => x.Date);
        var assessed = new List<DailyFailureRateDto>();
        foreach (var date in window)
        {
            if (!byDate.TryGetValue(date, out var day) || day.Results == 0)
            {
                assessed.Add(new DailyFailureRateDto(date, 0, 0, 0, null, null, 0, null, false, "No results"));
                continue;
            }

            var rate = (double)day.Failed / day.Results;
            var baseline = Enumerable.Range(1, baselineDays)
                .Select(offset => byDate.GetValueOrDefault(date.AddDays(-offset)))
                .Where(x => x is not null && x.Results >= minimumDailyResults)
                .Select(x => (double)x!.Failed / x.Results)
                .ToList();

            if (day.Results < minimumDailyResults)
            {
                assessed.Add(new DailyFailureRateDto(date, day.Results, day.Failed, Round(rate), null, null, baseline.Count, null, false,
                    $"Too few results to assess (minimum {minimumDailyResults})"));
                continue;
            }
            if (baseline.Count < minimumBaselineDays)
            {
                assessed.Add(new DailyFailureRateDto(date, day.Results, day.Failed, Round(rate), null, null, baseline.Count, null, false,
                    $"Insufficient baseline ({baseline.Count} of {minimumBaselineDays} required days)"));
                continue;
            }

            var mean = baseline.Average();
            var stdDev = Math.Max(MinimumStdDev, Math.Sqrt(baseline.Sum(x => (x - mean) * (x - mean)) / baseline.Count));
            var z = (rate - mean) / stdDev;
            var isAnomaly = Math.Abs(z) > threshold;
            var assessment = !isAnomaly ? "Within expected range"
                : z > 0 ? "Failure rate unusually high" : "Failure rate unusually low";
            assessed.Add(new DailyFailureRateDto(date, day.Results, day.Failed, Round(rate), Round(mean), Round(stdDev),
                baseline.Count, Math.Round(z, 2), isAnomaly, assessment));
        }
        return assessed;
    }

    /// <summary>Number of times consecutive executions switched between passed and failed (skips ignored).</summary>
    public static int CountOutcomeChanges(IEnumerable<TestOutcome> chronologicalOutcomes)
    {
        var changes = 0;
        TestOutcome? previous = null;
        foreach (var outcome in chronologicalOutcomes.Where(x => x != TestOutcome.Skipped))
        {
            if (previous is not null && previous != outcome) changes++;
            previous = outcome;
        }
        return changes;
    }

    public static double? ChangeInPercentagePoints(double current, double? previous) =>
        previous is null ? null : Math.Round((current - previous.Value) * 100, 1);

    private static double Round(double value) => Math.Round(value, 4);
}
