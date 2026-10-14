namespace CQPulse.Application.Simulation;

/// <summary>Simulator limits and tuning, bound from the "Simulator" configuration section.</summary>
public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    public int MaxTestCount { get; set; } = 50_000;
    public int MaxConcurrency { get; set; } = 128;
    public int MaxDurationMs { get; set; } = 60_000;
    /// <summary>Workloads at or above this size get a resource warning in the preview.</summary>
    public int LargeWorkloadThreshold { get; set; } = 10_000;
    /// <summary>Capacity of the in-process run queue; submissions wait when it is full.</summary>
    public int QueueCapacity { get; set; } = 100;
    /// <summary>Results buffered between test execution and persistence (backpressure bound).</summary>
    public int ResultBufferCapacity { get; set; } = 2_000;
    /// <summary>Results saved per database round trip, each with a fresh change tracker.</summary>
    public int ResultBatchSize { get; set; } = 500;
    /// <summary>Approximate number of progress notifications per run.</summary>
    public int ProgressUpdatesPerRun { get; set; } = 100;

    public IEnumerable<string> Validate()
    {
        if (MaxTestCount < 1) yield return $"{nameof(MaxTestCount)} must be positive.";
        if (MaxConcurrency is < 1 or > 1_024) yield return $"{nameof(MaxConcurrency)} must be between 1 and 1024.";
        if (MaxDurationMs < 0) yield return $"{nameof(MaxDurationMs)} must not be negative.";
        if (QueueCapacity < 1) yield return $"{nameof(QueueCapacity)} must be positive.";
        if (ResultBufferCapacity < 1) yield return $"{nameof(ResultBufferCapacity)} must be positive.";
        if (ResultBatchSize < 1) yield return $"{nameof(ResultBatchSize)} must be positive.";
        if (ProgressUpdatesPerRun < 1) yield return $"{nameof(ProgressUpdatesPerRun)} must be positive.";
    }
}
