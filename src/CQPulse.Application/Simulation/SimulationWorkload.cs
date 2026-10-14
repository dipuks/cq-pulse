using CQPulse.Domain;

namespace CQPulse.Application.Simulation;

public readonly record struct OutcomeAllocation(int Passed, int Failed, int Skipped);

public readonly record struct SimulatedTest(int Index, TestOutcome Outcome, int DurationMs, FailureCategory? FailureCategory);

/// <summary>
/// Generates a simulated workload one test at a time, in constant memory.
/// Outcome totals are exact (largest-remainder allocation of the configured percentages); which test gets
/// which outcome is decided by a seeded affine permutation, and per-test duration and failure category come
/// from a per-index random stream. The same request and seed always produce the same workload, in any process.
/// </summary>
public sealed class SimulationWorkload
{
    private readonly SimulationRequest _request;
    private readonly int _seed;
    private readonly long _multiplier;
    private readonly long _offset;

    public SimulationWorkload(SimulationRequest request, int seed)
    {
        _request = request;
        _seed = seed;
        Allocation = Allocate(request.TestCount, request.PassPercentage, request.FailurePercentage, request.SkippedPercentage);
        (_multiplier, _offset) = ChoosePermutation(request.TestCount, seed);
    }

    public OutcomeAllocation Allocation { get; }

    /// <summary>Splits <paramref name="total"/> by percentage so the parts always sum to the total.</summary>
    public static OutcomeAllocation Allocate(int total, int passPercentage, int failurePercentage, int skippedPercentage)
    {
        int[] percentages = [passPercentage, failurePercentage, skippedPercentage];
        var counts = percentages.Select(p => (int)((long)total * p / 100)).ToArray();
        var leftover = total - counts.Sum();
        foreach (var i in Enumerable.Range(0, 3).OrderByDescending(i => (long)total * percentages[i] % 100).ThenBy(i => i).Take(leftover))
            counts[i]++;
        return new OutcomeAllocation(counts[0], counts[1], counts[2]);
    }

    /// <param name="index">Zero-based test index, less than the test count.</param>
    public SimulatedTest Generate(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _request.TestCount);

        var slot = (_multiplier * index + _offset) % _request.TestCount;
        var outcome = slot < Allocation.Passed ? TestOutcome.Passed
            : slot < Allocation.Passed + Allocation.Failed ? TestOutcome.Failed
            : TestOutcome.Skipped;

        var random = new Random(StableSeed(_seed, index));
        var duration = random.Next(_request.MinimumDurationMs, _request.MaximumDurationMs + 1);
        var categories = _request.FailureCategories;
        FailureCategory? category = outcome == TestOutcome.Failed && categories is { Count: > 0 }
            ? categories[random.Next(categories.Count)]
            : null;
        return new SimulatedTest(index, outcome, duration, category);
    }

    /// <summary>SplitMix64 finalizer over (seed, index): stable across processes, unlike HashCode.Combine.</summary>
    public static int StableSeed(int seed, int index)
    {
        unchecked
        {
            var z = ((ulong)(uint)seed << 32) | (uint)index;
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)(z ^ (z >> 31));
        }
    }

    private static (long Multiplier, long Offset) ChoosePermutation(int count, int seed)
    {
        if (count <= 1) return (1, 0);
        var random = new Random(StableSeed(seed, -1));
        // A multiplier coprime with the count makes i -> (a*i + b) mod n a bijection. Skipping small
        // multipliers avoids long same-outcome stretches, so partial (cancelled) runs stay representative.
        var minimum = Math.Max(1, count / 3);
        long multiplier;
        do multiplier = random.Next(minimum, count);
        while (GreatestCommonDivisor(multiplier, count) != 1);
        return (multiplier, random.Next(count));
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
