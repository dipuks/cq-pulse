using CQPulse.Domain;

namespace CQPulse.Application.Simulation;

public readonly record struct OutcomeAllocation(int Passed, int Failed, int Skipped)
{
    public static OutcomeAllocation operator +(OutcomeAllocation a, OutcomeAllocation b) =>
        new(a.Passed + b.Passed, a.Failed + b.Failed, a.Skipped + b.Skipped);
}

/// <summary>An enabled catalog test case as the simulator executes it.</summary>
public sealed record CatalogCase(Guid Id, string Name, SimulatedOutcome Outcome, int DurationMs);

public readonly record struct SimulatedTest(
    int Index, TestOutcome Outcome, int DurationMs, FailureCategory? FailureCategory, Guid? TestCaseId, string Name);

/// <summary>
/// Generates a simulated workload one test at a time, in constant memory (beyond the catalog case list).
/// <para>
/// Synthetic mode: every test takes its outcome from the configured percentages.
/// Catalog mode: enabled test cases are executed in order, cycling until the test count is reached; a case with a
/// fixed simulated outcome always produces it and uses its configured duration, and only <c>Random</c> cases take
/// outcomes from the percentages.
/// </para>
/// Percentage-driven outcomes are allocated exactly (largest remainder) and assigned through a seeded affine
/// permutation; durations and failure categories come from per-index random streams. The same request, seed and
/// catalog always produce the same workload, in any process.
/// </summary>
public sealed class SimulationWorkload
{
    private readonly SimulationRequest _request;
    private readonly int _seed;
    private readonly IReadOnlyList<CatalogCase>? _cases;
    private readonly int[] _randomPosition = [];
    private readonly int _randomCasesPerCycle;
    private readonly int _randomTestCount;
    private readonly long _multiplier;
    private readonly long _offset;

    public SimulationWorkload(SimulationRequest request, int seed, IReadOnlyList<CatalogCase>? catalogCases = null)
    {
        _request = request;
        _seed = seed;
        if (catalogCases is { Count: > 0 })
        {
            _cases = catalogCases;
            _randomPosition = new int[catalogCases.Count];
            var fixedCounts = new OutcomeAllocation();
            for (var c = 0; c < catalogCases.Count; c++)
            {
                var occurrences = Occurrences(c);
                switch (catalogCases[c].Outcome)
                {
                    case SimulatedOutcome.Random:
                        _randomPosition[c] = _randomCasesPerCycle++;
                        _randomTestCount += occurrences;
                        break;
                    case SimulatedOutcome.Passed: fixedCounts += new OutcomeAllocation(occurrences, 0, 0); break;
                    case SimulatedOutcome.Failed: fixedCounts += new OutcomeAllocation(0, occurrences, 0); break;
                    default: fixedCounts += new OutcomeAllocation(0, 0, occurrences); break;
                }
            }
            RandomAllocation = Allocate(_randomTestCount, request.PassPercentage, request.FailurePercentage, request.SkippedPercentage);
            Allocation = fixedCounts + RandomAllocation;
        }
        else
        {
            _randomTestCount = request.TestCount;
            RandomAllocation = Allocate(request.TestCount, request.PassPercentage, request.FailurePercentage, request.SkippedPercentage);
            Allocation = RandomAllocation;
        }
        (_multiplier, _offset) = ChoosePermutation(_randomTestCount, seed);
    }

    /// <summary>Exact final outcome totals for the whole run.</summary>
    public OutcomeAllocation Allocation { get; }

    /// <summary>The part of <see cref="Allocation"/> driven by the configured percentages.</summary>
    public OutcomeAllocation RandomAllocation { get; }

    public bool UsesCatalog => _cases is not null;

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

        var random = new Random(StableSeed(_seed, index));
        CatalogCase? testCase = null;
        TestOutcome outcome;
        int duration;
        if (_cases is null)
        {
            outcome = RandomOutcome(index);
            duration = random.Next(_request.MinimumDurationMs, _request.MaximumDurationMs + 1);
        }
        else
        {
            var caseIndex = index % _cases.Count;
            testCase = _cases[caseIndex];
            outcome = testCase.Outcome switch
            {
                SimulatedOutcome.Passed => TestOutcome.Passed,
                SimulatedOutcome.Failed => TestOutcome.Failed,
                SimulatedOutcome.Skipped => TestOutcome.Skipped,
                _ => RandomOutcome(index / _cases.Count * _randomCasesPerCycle + _randomPosition[caseIndex])
            };
            duration = testCase.DurationMs;
        }

        var categories = _request.FailureCategories;
        FailureCategory? category = outcome == TestOutcome.Failed && categories is { Count: > 0 }
            ? categories[random.Next(categories.Count)]
            : null;
        return new SimulatedTest(index, outcome, duration, category, testCase?.Id, testCase?.Name ?? $"Generated test {index + 1:N0}");
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

    private int Occurrences(int caseIndex) =>
        _request.TestCount / _cases!.Count + (caseIndex < _request.TestCount % _cases.Count ? 1 : 0);

    /// <param name="rank">Position among percentage-driven tests, less than the number of such tests.</param>
    private TestOutcome RandomOutcome(int rank)
    {
        var slot = (_multiplier * rank + _offset) % _randomTestCount;
        return slot < RandomAllocation.Passed ? TestOutcome.Passed
            : slot < RandomAllocation.Passed + RandomAllocation.Failed ? TestOutcome.Failed
            : TestOutcome.Skipped;
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
