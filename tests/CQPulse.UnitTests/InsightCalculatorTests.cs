using CQPulse.Application.Analysis;
using CQPulse.Application.Insights;
using CQPulse.Domain;
using static CQPulse.Application.Insights.InsightCalculator;

namespace CQPulse.UnitTests;

public sealed class InsightCalculatorTests
{
    private static readonly DateOnly Today = new(2030, 3, 31);

    private static List<DailyCount> Baseline(int days, int results, Func<int, int> failed) =>
        Enumerable.Range(1, days).Select(i => new DailyCount(Today.AddDays(-i), results, failed(i))).ToList();

    [Fact]
    public void Spike_above_threshold_is_flagged_with_its_evidence()
    {
        // Baseline alternates 10% and 12% failure (mean 11%, sd 1%); today 30%.
        var history = Baseline(10, 100, i => i % 2 == 0 ? 10 : 12);
        history.Add(new DailyCount(Today, 100, 30));

        var day = DetectAnomalies(history, [Today], 10, 5, 20, 3).Single();

        Assert.True(day.IsAnomaly);
        Assert.Equal("Failure rate unusually high", day.Assessment);
        Assert.Equal((0.30, 0.11, 0.01, 10), (day.FailureRate, day.BaselineMean!.Value, day.BaselineStdDev!.Value, day.BaselineDays));
        Assert.Equal(19, day.ZScore!.Value, 1);
    }

    [Fact]
    public void Normal_variation_is_not_flagged()
    {
        var history = Baseline(10, 100, i => i % 2 == 0 ? 10 : 12);
        history.Add(new DailyCount(Today, 100, 12));

        var day = DetectAnomalies(history, [Today], 10, 5, 20, 3).Single();

        Assert.False(day.IsAnomaly);
        Assert.Equal("Within expected range", day.Assessment);
    }

    [Fact]
    public void Flat_baseline_uses_a_standard_deviation_floor()
    {
        var history = Baseline(7, 100, _ => 10);
        history.Add(new DailyCount(Today, 100, 12)); // +2 points against sd floor 1 point: z = 2

        var day = DetectAnomalies(history, [Today], 7, 5, 20, 3).Single();

        Assert.Equal(MinimumStdDev, day.BaselineStdDev);
        Assert.Equal(2, day.ZScore!.Value, 6);
        Assert.False(day.IsAnomaly);
    }

    [Fact]
    public void Drops_are_flagged_as_unusually_low()
    {
        var history = Baseline(10, 100, i => i % 2 == 0 ? 40 : 42);
        history.Add(new DailyCount(Today, 100, 5));

        Assert.Equal("Failure rate unusually low", DetectAnomalies(history, [Today], 10, 5, 20, 3).Single().Assessment);
    }

    [Fact]
    public void Insufficient_data_is_reported_instead_of_guessing()
    {
        var history = Baseline(3, 100, _ => 10);                 // only 3 baseline days
        history.Add(new DailyCount(Today, 100, 90));
        history.Add(new DailyCount(Today.AddDays(1), 5, 5));     // too few results
        var days = DetectAnomalies(history, [Today, Today.AddDays(1), Today.AddDays(2)], 10, 5, 20, 3);

        Assert.Equal("Insufficient baseline (3 of 5 required days)", days[0].Assessment);
        Assert.Equal("Too few results to assess (minimum 20)", days[1].Assessment);
        Assert.Equal("No results", days[2].Assessment);
        Assert.All(days, d => Assert.False(d.IsAnomaly));
        Assert.All(days, d => Assert.Null(d.ZScore));
    }

    [Fact]
    public void Baseline_ignores_days_below_the_minimum_volume()
    {
        var history = Baseline(10, 100, _ => 10);
        history[0] = history[0] with { Results = 3, Failed = 3 }; // yesterday: 3 results, 100% — excluded from baseline
        history.Add(new DailyCount(Today, 100, 10));

        var day = DetectAnomalies(history, [Today], 10, 5, 20, 3).Single();

        Assert.Equal((9, 0.1), (day.BaselineDays, day.BaselineMean!.Value));
    }

    [Theory]
    [InlineData(new[] { TestOutcome.Passed, TestOutcome.Passed, TestOutcome.Passed }, 0)]
    [InlineData(new[] { TestOutcome.Passed, TestOutcome.Failed, TestOutcome.Passed, TestOutcome.Failed }, 3)]
    [InlineData(new[] { TestOutcome.Passed, TestOutcome.Skipped, TestOutcome.Failed, TestOutcome.Skipped, TestOutcome.Failed }, 1)]
    public void Outcome_changes_ignore_skips(TestOutcome[] outcomes, int expected) =>
        Assert.Equal(expected, CountOutcomeChanges(outcomes));

    [Fact]
    public void Change_in_percentage_points_requires_a_previous_value()
    {
        Assert.Equal(5.0, ChangeInPercentagePoints(0.15, 0.10));
        Assert.Null(ChangeInPercentagePoints(0.15, null));
    }

    [Theory]
    [InlineData("Timeout after 3000 ms for order 42", "Timeout after <n> ms for order <n>")]
    [InlineData("User 'alice' not found in 1f0e2c3d-1111-2222-3333-444455556666", "User <value> not found in <id>")]
    [InlineData("  Element   #submit\tnot found ", "Element #submit not found")]
    [InlineData(null, "(no message)")]
    public void Failure_messages_are_normalized(string? message, string expected) =>
        Assert.Equal(expected, FailureSignature.Normalize(message));

    [Fact]
    public void Signatures_group_variants_and_separate_categories()
    {
        Assert.Equal(FailureSignature.Compute(FailureCategory.Timeout, "Timeout after 3000 ms"), FailureSignature.Compute(FailureCategory.Timeout, "Timeout after 5000 ms"));
        Assert.NotEqual(FailureSignature.Compute(FailureCategory.Timeout, "Timeout after 3000 ms"), FailureSignature.Compute(FailureCategory.ApiError, "Timeout after 3000 ms"));
        Assert.Matches("^[0-9a-f]{12}$", FailureSignature.Compute(null, "x"));
    }
}
