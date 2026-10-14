using CQPulse.Application.Common;
using CQPulse.Application.Simulation;
using CQPulse.Domain;

namespace CQPulse.UnitTests;

public sealed class SimulationRequestValidatorTests
{
    private static readonly SimulatorOptions Limits = new() { MaxTestCount = 1_000, MaxConcurrency = 16, MaxDurationMs = 5_000 };

    private static readonly SimulationRequest Valid = new(
        Guid.NewGuid(), null, 100, 8, 10, 100, 80, 15, 5, [FailureCategory.Timeout], 42, "Nightly");

    [Fact]
    public void Valid_request_passes() => SimulationRequestValidator.Validate(Valid, Limits);

    [Fact]
    public void Limits_are_inclusive() =>
        SimulationRequestValidator.Validate(Valid with { TestCount = 1_000, MaxConcurrency = 16, MaximumDurationMs = 5_000 }, Limits);

    public static TheoryData<SimulationRequest, string> InvalidRequests => new()
    {
        { Valid with { ProjectId = Guid.Empty }, "ProjectId" },
        { Valid with { TestCount = 0 }, "TestCount" },
        { Valid with { TestCount = 1_001 }, "TestCount" },
        { Valid with { MaxConcurrency = 0 }, "MaxConcurrency" },
        { Valid with { MaxConcurrency = 17 }, "MaxConcurrency" },
        { Valid with { MinimumDurationMs = -1 }, "MinimumDurationMs" },
        { Valid with { MinimumDurationMs = 200, MaximumDurationMs = 100 }, "MaximumDurationMs" },
        { Valid with { MaximumDurationMs = 5_001 }, "MaximumDurationMs" },
        { Valid with { PassPercentage = 81 }, "Percentages" },
        { Valid with { PassPercentage = 105, FailurePercentage = -10 }, "PassPercentage" },
        { Valid with { FailureCategories = [] }, "FailureCategories" },
        { Valid with { FailureCategories = null }, "FailureCategories" },
        { Valid with { FailureCategories = [(FailureCategory)99] }, "FailureCategories" },
        { Valid with { RunLabel = new string('x', 201) }, "RunLabel" }
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void Invalid_field_is_reported(SimulationRequest request, string field)
    {
        var exception = Assert.Throws<ValidationException>(() => SimulationRequestValidator.Validate(request, Limits));

        Assert.Contains(field, exception.Errors.Keys);
    }

    [Fact]
    public void Failure_categories_are_optional_without_failures() =>
        SimulationRequestValidator.Validate(Valid with { PassPercentage = 95, FailurePercentage = 0, FailureCategories = [] }, Limits);

    [Fact]
    public void All_errors_are_reported_together()
    {
        var request = Valid with { ProjectId = Guid.Empty, TestCount = 0, MaxConcurrency = 0 };

        var exception = Assert.Throws<ValidationException>(() => SimulationRequestValidator.Validate(request, Limits));

        Assert.Equal(["MaxConcurrency", "ProjectId", "TestCount"], exception.Errors.Keys.Order());
    }
}
