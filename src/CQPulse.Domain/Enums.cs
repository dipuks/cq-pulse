namespace CQPulse.Domain;

public enum ProjectStatus { Active, Archived }
public enum TestPriority { Low, Medium, High, Critical }
public enum SimulatedOutcome { Random, Passed, Failed, Skipped }
public enum TestOutcome { Passed, Failed, Skipped }
public enum RunStatus { Queued, Running, Passed, CompletedWithFailures, Failed, Cancelled }
public enum WorkloadSource { Synthetic, Catalog }
public enum FailureCategory { UiElement, ApiError, Timeout, DataValidation, Environment }
public enum LogLevel { Trace, Information, Warning, Error }
