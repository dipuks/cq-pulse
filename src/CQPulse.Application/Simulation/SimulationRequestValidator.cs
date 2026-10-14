using CQPulse.Application.Common;
using CQPulse.Domain;

namespace CQPulse.Application.Simulation;

public static class SimulationRequestValidator
{
    public const int MaxRunLabelLength = 200;

    /// <summary>Throws <see cref="ValidationException"/> listing every invalid field.</summary>
    public static void Validate(SimulationRequest request, SimulatorOptions limits)
    {
        var categories = request.FailureCategories ?? [];
        new ValidationErrors()
            .AddIf(request.ProjectId == Guid.Empty, nameof(request.ProjectId), "A project is required.")
            .AddIf(request.TestCount < 1 || request.TestCount > limits.MaxTestCount, nameof(request.TestCount),
                $"Test count must be between 1 and {limits.MaxTestCount:N0}.")
            .AddIf(request.MaxConcurrency < 1 || request.MaxConcurrency > limits.MaxConcurrency, nameof(request.MaxConcurrency),
                $"Concurrency must be between 1 and {limits.MaxConcurrency}.")
            .AddIf(request.MinimumDurationMs < 0, nameof(request.MinimumDurationMs), "Minimum duration must not be negative.")
            .AddIf(request.MaximumDurationMs < request.MinimumDurationMs, nameof(request.MaximumDurationMs),
                "Maximum duration must be greater than or equal to the minimum duration.")
            .AddIf(request.MaximumDurationMs > limits.MaxDurationMs, nameof(request.MaximumDurationMs),
                $"Maximum duration must not exceed {limits.MaxDurationMs:N0} ms.")
            .AddIf(request.PassPercentage is < 0 or > 100, nameof(request.PassPercentage), "Pass percentage must be between 0 and 100.")
            .AddIf(request.FailurePercentage is < 0 or > 100, nameof(request.FailurePercentage), "Failure percentage must be between 0 and 100.")
            .AddIf(request.SkippedPercentage is < 0 or > 100, nameof(request.SkippedPercentage), "Skipped percentage must be between 0 and 100.")
            .AddIf(request.PassPercentage + request.FailurePercentage + request.SkippedPercentage != 100, "Percentages",
                "Pass, failure and skipped percentages must total 100.")
            .AddIf(request.FailurePercentage > 0 && categories.Count == 0, nameof(request.FailureCategories),
                "At least one failure category is required when failures are configured.")
            .AddIf(categories.Any(x => !Enum.IsDefined(x)), nameof(request.FailureCategories), "Unknown failure category.")
            .AddIf(!Enum.IsDefined(request.WorkloadSource), nameof(request.WorkloadSource), "Unknown workload source.")
            .AddIf(request.RunLabel?.Trim().Length > MaxRunLabelLength, nameof(request.RunLabel),
                $"Run label must not exceed {MaxRunLabelLength} characters.")
            .ThrowIfAny();
    }
}
