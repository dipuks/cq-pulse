// API contracts. Enum values arrive as their C# names (JsonStringEnumConverter on both REST and SignalR).

export type ProjectStatus = 'Active' | 'Archived';
export type TestPriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type SimulatedOutcome = 'Random' | 'Passed' | 'Failed' | 'Skipped';
export type TestOutcome = 'Passed' | 'Failed' | 'Skipped';
export type RunStatus = 'Queued' | 'Running' | 'Passed' | 'CompletedWithFailures' | 'Failed' | 'Cancelled';
export type FailureCategory = 'UiElement' | 'ApiError' | 'Timeout' | 'DataValidation' | 'Environment';
export type WorkloadSource = 'Synthetic' | 'Catalog';

export const RUN_STATUSES: RunStatus[] = ['Queued', 'Running', 'Passed', 'CompletedWithFailures', 'Failed', 'Cancelled'];
export const TEST_OUTCOMES: TestOutcome[] = ['Passed', 'Failed', 'Skipped'];
export const PRIORITIES: TestPriority[] = ['Low', 'Medium', 'High', 'Critical'];
export const SIMULATED_OUTCOMES: SimulatedOutcome[] = ['Random', 'Passed', 'Failed', 'Skipped'];
export const FAILURE_CATEGORIES: FailureCategory[] = ['UiElement', 'ApiError', 'Timeout', 'DataValidation', 'Environment'];

export const isTerminal = (status: RunStatus | string): boolean =>
  status === 'Passed' || status === 'CompletedWithFailures' || status === 'Failed' || status === 'Cancelled';

export interface PageResponse<T> { items: T[]; totalCount: number; page: number; pageSize: number; }

export interface Project { id: string; name: string; description?: string | null; status: ProjectStatus; createdAtUtc: string; }
export interface SaveProject { name: string; description?: string | null; status: ProjectStatus; }

export interface Suite { id: string; projectId: string; name: string; description?: string | null; testCaseCount: number; enabledTestCaseCount: number; }
export interface SaveSuite { name: string; description?: string | null; }

export interface TestCase {
  id: string; testSuiteId: string; name: string; description?: string | null; enabled: boolean;
  priority: TestPriority; simulatedOutcome: SimulatedOutcome; simulatedDurationMs: number;
}
export type SaveTestCase = Omit<TestCase, 'id' | 'testSuiteId'>;

export interface RunSummary {
  id: string; projectId: string; testSuiteId?: string | null; runLabel: string; status: RunStatus;
  requestedAtUtc: string; startedAtUtc?: string | null; completedAtUtc?: string | null;
  totalTests: number; completedTests: number; passedCount: number; failedCount: number; skippedCount: number;
  errorSummary?: string | null; rerunOfRunId?: string | null;
}
export interface RunDetail extends RunSummary {
  configurationSnapshot: string; randomSeed?: number | null; cancellationReason?: string | null; durationMs?: number | null;
  rerunIds: string[];
}
export interface RunResult {
  id: string; testCaseNameSnapshot: string; outcome: TestOutcome; durationMs: number;
  startedAtUtc: string; completedAtUtc: string; errorMessage?: string | null; failureCategory?: FailureCategory | null;
}
export interface RunLog { id: string; timestampUtc: string; level: string; message: string; metadataJson?: string | null; }
export interface RunProgress {
  runId: string; status: RunStatus; totalTests: number; completedTests: number;
  passedCount: number; failedCount: number; skippedCount: number; currentTestName?: string | null;
}

/** Catalog counts are unique items; passed/failed/skipped are historical executions across all runs. */
export interface DashboardSummary {
  projectCount: number; testSuiteCount: number; testCaseCount: number; totalRuns: number;
  passed: number; failed: number; skipped: number; activeRuns: number; passRate: number;
}
export interface AnalyticsSummary {
  total: number; passed: number; failed: number; skipped: number; passRate: number; failureRate: number; averageDurationMs?: number | null;
}
export interface TrendPoint { date: string; passed: number; failed: number; skipped: number; averageDurationMs: number; }
export interface FailureCount { category: FailureCategory; count: number; }

export interface SimulationRequest {
  projectId: string; testSuiteId: string | null; testCount: number; maxConcurrency: number;
  minimumDurationMs: number; maximumDurationMs: number;
  passPercentage: number; failurePercentage: number; skippedPercentage: number;
  failureCategories: FailureCategory[]; randomSeed: number | null; runLabel: string; workloadSource: WorkloadSource;
}
export interface SimulationPreview {
  testCount: number; expectedPassed: number; expectedFailed: number; expectedSkipped: number; maxConcurrency: number;
  resourceWarning: string; projectName: string; testSuiteName?: string | null; minimumDurationMs: number; maximumDurationMs: number;
  failureCategories: FailureCategory[]; randomSeed?: number | null; runLabel?: string | null; estimatedDurationSeconds: number;
  workloadSource: WorkloadSource; catalogCaseCount: number; workloadDescription?: string | null;
}
export interface SimulatorLimits { maxTestCount: number; maxConcurrency: number; maxDurationMs: number; largeWorkloadThreshold: number; }

export interface ProjectSummary {
  id: string; name: string; description?: string | null; status: ProjectStatus; createdAtUtc: string;
  suiteCount: number; testCaseCount: number; enabledTestCaseCount: number; runCount: number; activeRunCount: number;
  passedResults: number; failedResults: number; skippedResults: number; passRate: number; recentRuns: RunSummary[];
}
export interface TestCaseExecution {
  resultId: string; runId: string; runLabel: string; testCaseNameSnapshot: string; outcome: TestOutcome; durationMs: number;
  completedAtUtc: string; errorMessage?: string | null; failureCategory?: FailureCategory | null;
}
