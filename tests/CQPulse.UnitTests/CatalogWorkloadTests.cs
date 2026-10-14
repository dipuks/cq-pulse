using CQPulse.Application.Simulation;
using CQPulse.Domain;

namespace CQPulse.UnitTests;

public sealed class CatalogWorkloadTests
{
    private static readonly CatalogCase PassCase = new(Guid.NewGuid(), "Valid login", SimulatedOutcome.Passed, 10);
    private static readonly CatalogCase FailCase = new(Guid.NewGuid(), "Invalid password", SimulatedOutcome.Failed, 20);
    private static readonly CatalogCase SkipCase = new(Guid.NewGuid(), "Legacy browser", SimulatedOutcome.Skipped, 5);
    private static readonly CatalogCase RandomA = new(Guid.NewGuid(), "Search", SimulatedOutcome.Random, 30);
    private static readonly CatalogCase RandomB = new(Guid.NewGuid(), "Checkout", SimulatedOutcome.Random, 40);

    [Fact]
    public void Cases_are_executed_in_order_and_cycled_to_the_test_count()
    {
        CatalogCase[] cases = [PassCase, FailCase, RandomA];
        var workload = new SimulationWorkload(Request(7), 1, cases);

        var ids = Enumerable.Range(0, 7).Select(i => workload.Generate(i).TestCaseId).ToList();

        Assert.Equal([PassCase.Id, FailCase.Id, RandomA.Id, PassCase.Id, FailCase.Id, RandomA.Id, PassCase.Id], ids);
    }

    [Fact]
    public void Fixed_outcomes_and_case_durations_are_honoured()
    {
        CatalogCase[] cases = [PassCase, FailCase, SkipCase];
        var tests = Generate(Request(30), cases);

        Assert.All(tests.Where(t => t.TestCaseId == PassCase.Id), t => Assert.Equal((TestOutcome.Passed, 10), (t.Outcome, t.DurationMs)));
        Assert.All(tests.Where(t => t.TestCaseId == FailCase.Id), t => Assert.Equal((TestOutcome.Failed, 20), (t.Outcome, t.DurationMs)));
        Assert.All(tests.Where(t => t.TestCaseId == SkipCase.Id), t => Assert.Equal((TestOutcome.Skipped, 5), (t.Outcome, t.DurationMs)));
        Assert.All(tests, t => Assert.Equal(cases.Single(c => c.Id == t.TestCaseId).Name, t.Name));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(1_003)]
    public void Allocation_is_exact_for_mixed_fixed_and_random_cases(int testCount)
    {
        CatalogCase[] cases = [PassCase, RandomA, FailCase, RandomB, SkipCase];
        var request = Request(testCount);
        var workload = new SimulationWorkload(request, 99, cases);

        var tests = Enumerable.Range(0, testCount).Select(workload.Generate).ToList();
        var actual = new OutcomeAllocation(
            tests.Count(t => t.Outcome == TestOutcome.Passed), tests.Count(t => t.Outcome == TestOutcome.Failed), tests.Count(t => t.Outcome == TestOutcome.Skipped));

        Assert.Equal(workload.Allocation, actual);
        var randomTests = tests.Count(t => t.TestCaseId == RandomA.Id || t.TestCaseId == RandomB.Id);
        Assert.Equal(SimulationWorkload.Allocate(randomTests, 60, 30, 10), workload.RandomAllocation);
    }

    [Fact]
    public void Catalog_workload_is_reproducible_for_the_same_seed()
    {
        CatalogCase[] cases = [RandomA, RandomB, PassCase];

        Assert.Equal(Generate(Request(200), cases, seed: 5), Generate(Request(200), cases, seed: 5));
    }

    [Fact]
    public void Synthetic_tests_have_no_case_identity()
    {
        var test = new SimulationWorkload(Request(3), 1).Generate(2);

        Assert.Null(test.TestCaseId);
        Assert.Equal("Generated test 3", test.Name);
    }

    private static List<SimulatedTest> Generate(SimulationRequest request, IReadOnlyList<CatalogCase> cases, int seed = 3)
    {
        var workload = new SimulationWorkload(request, seed, cases);
        return Enumerable.Range(0, request.TestCount).Select(workload.Generate).ToList();
    }

    private static SimulationRequest Request(int testCount) =>
        new(Guid.NewGuid(), null, testCount, 4, 0, 100, 60, 30, 10, [FailureCategory.Timeout], 3, "catalog", WorkloadSource.Catalog);
}
