using CQPulse.Application.Simulation;
using CQPulse.Domain;

namespace CQPulse.UnitTests;

public sealed class SimulationWorkloadTests
{
    [Theory]
    [InlineData(100, 88, 8, 4, 88, 8, 4)]
    [InlineData(7, 50, 25, 25, 3, 2, 2)]
    [InlineData(1, 33, 33, 34, 0, 0, 1)]
    [InlineData(3, 34, 33, 33, 1, 1, 1)]
    [InlineData(10_000, 100, 0, 0, 10_000, 0, 0)]
    [InlineData(50_000, 79, 16, 5, 39_500, 8_000, 2_500)]
    public void Allocate_splits_exactly_by_largest_remainder(
        int total, int pass, int fail, int skip, int expectedPassed, int expectedFailed, int expectedSkipped)
    {
        var allocation = SimulationWorkload.Allocate(total, pass, fail, skip);

        Assert.Equal(new OutcomeAllocation(expectedPassed, expectedFailed, expectedSkipped), allocation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(997)]
    [InlineData(1_000)]
    [InlineData(4_096)]
    public void Generated_outcomes_match_the_allocation_exactly(int testCount)
    {
        var workload = new SimulationWorkload(Request(testCount, seed: 7), 7);

        var counts = Enumerable.Range(0, testCount).Select(workload.Generate)
            .GroupBy(x => x.Outcome).ToDictionary(x => x.Key, x => x.Count());

        Assert.Equal(workload.Allocation.Passed, counts.GetValueOrDefault(TestOutcome.Passed));
        Assert.Equal(workload.Allocation.Failed, counts.GetValueOrDefault(TestOutcome.Failed));
        Assert.Equal(workload.Allocation.Skipped, counts.GetValueOrDefault(TestOutcome.Skipped));
    }

    [Fact]
    public void Same_seed_produces_the_same_workload()
    {
        var first = Generate(Request(500, seed: 1234), 1234);
        var second = Generate(Request(500, seed: 1234), 1234);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_produce_different_workloads()
    {
        var first = Generate(Request(500, seed: 1), 1);
        var second = Generate(Request(500, seed: 2), 2);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Pinned values guard cross-process reproducibility: a per-process hash (such as HashCode.Combine)
    /// would change these on every run.
    /// </summary>
    [Fact]
    public void Stable_seed_and_seeded_workload_match_pinned_values()
    {
        Assert.Equal(-1096032325, SimulationWorkload.StableSeed(42, 0));
        Assert.Equal(1616377404, SimulationWorkload.StableSeed(42, 1));
        Assert.Equal(-790314536, SimulationWorkload.StableSeed(-7, 123));

        var request = new SimulationRequest(Guid.NewGuid(), null, 10, 4, 10, 90, 60, 30, 10,
            [FailureCategory.Timeout, FailureCategory.ApiError], 42, null);
        var workload = new SimulationWorkload(request, 42);
        var actual = string.Join(" | ", Enumerable.Range(0, 10).Select(workload.Generate)
            .Select(t => $"{t.Outcome}/{t.DurationMs}/{t.FailureCategory}"));

        Assert.Equal(
            "Passed/48/ | Failed/79/Timeout | Passed/55/ | Passed/62/ | Failed/64/Timeout | Passed/83/ | " +
            "Passed/13/ | Skipped/11/ | Failed/57/ApiError | Passed/64/", actual);
    }

    [Fact]
    public void Durations_and_failure_categories_respect_the_request()
    {
        var categories = new[] { FailureCategory.Timeout, FailureCategory.Environment };
        var request = Request(2_000, seed: 99) with { MinimumDurationMs = 15, MaximumDurationMs = 40, FailureCategories = categories };

        var tests = Generate(request, 99);

        Assert.All(tests, t => Assert.InRange(t.DurationMs, 15, 40));
        Assert.All(tests.Where(t => t.Outcome == TestOutcome.Failed), t => Assert.Contains(t.FailureCategory!.Value, categories));
        Assert.All(tests.Where(t => t.Outcome != TestOutcome.Failed), t => Assert.Null(t.FailureCategory));
    }

    [Fact]
    public void Generate_rejects_out_of_range_indexes()
    {
        var workload = new SimulationWorkload(Request(5, seed: 1), 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => workload.Generate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => workload.Generate(5));
    }

    private static List<SimulatedTest> Generate(SimulationRequest request, int seed)
    {
        var workload = new SimulationWorkload(request, seed);
        return Enumerable.Range(0, request.TestCount).Select(workload.Generate).ToList();
    }

    private static SimulationRequest Request(int testCount, int seed) =>
        new(Guid.NewGuid(), null, testCount, 8, 0, 100, 70, 20, 10,
            [FailureCategory.UiElement, FailureCategory.ApiError, FailureCategory.Timeout], seed, "unit");
}
