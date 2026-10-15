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

export type AnalysisStatus = 'Pending' | 'Running' | 'Succeeded' | 'Failed' | 'Unavailable';
export interface AnalysisFinding {
  lineNumber: number; endLineNumber: number; timestamp?: string | null; message: string; severity: string; category: string; evidence: string; suggestion: string;
}
/** External diagnostic analysis. Severity, signal score and findings are the provider's output, shown unchanged. */
export interface FailureAnalysis {
  id: string; testRunId: string; provider: string; status: AnalysisStatus;
  requestedAtUtc: string; startedAtUtc?: string | null; completedAtUtc?: string | null; attempts: number; lastError?: string | null;
  inputFailureCount: number; inputTruncated: boolean; correlationId: string; externalAnalysisId?: string | null;
  severity?: string | null; summary?: string | null; signalScore?: number | null; topCategory?: string | null;
  findings: AnalysisFinding[]; recommendations: string[];
}
export interface IntegrationStatus { name: string; configured: boolean; status: string; detail?: string | null; baseUrl?: string | null; checkedAtUtc: string; }

export interface InsightWindow { fromUtc: string; toUtc: string; previousFromUtc: string; }
export interface FailureSignatureItem {
  signature: string; category?: FailureCategory | null; normalizedMessage: string; sampleMessage: string; occurrences: number; previousOccurrences: number;
  affectedRuns: number; affectedTestCases: number; firstSeenUtc: string; lastSeenUtc: string;
}
/** Counts are exact over signed failures; unsignedFailures are still waiting for the signature backfill. */
export interface FailureSignatureReport {
  window: InsightWindow; totalFailures: number; signatures: FailureSignatureItem[];
  distinctSignatures: number; unsignedFailures: number; previousUnsignedFailures: number; countsComplete: boolean;
}
export interface FlakyCandidate {
  testCaseId: string; testCaseName: string; testSuiteId: string; suiteName: string; configuredOutcome: SimulatedOutcome;
  executions: number; passed: number; failed: number; skipped: number; failureRate: number; outcomeChanges: number; randomnessIsConfigured: boolean;
}
export interface FlakyReport { window: InsightWindow; minimumExecutions: number; casesEvaluated: number; candidates: FlakyCandidate[]; }
export interface DailyFailureRate {
  date: string; results: number; failed: number; failureRate: number; baselineMean?: number | null; baselineStdDev?: number | null;
  baselineDays: number; zScore?: number | null; isAnomaly: boolean; assessment: string;
}
export interface AnomalyReport {
  window: InsightWindow; baselineDays: number; minimumBaselineDays: number; minimumDailyResults: number; threshold: number; method: string; days: DailyFailureRate[];
}
export interface QualityTrend {
  projectId: string; projectName: string; results: number; failed: number; failureRate: number;
  previousResults: number; previousFailed: number; previousFailureRate?: number | null; changePercentagePoints?: number | null;
}
export interface QualityTrendReport { window: InsightWindow; projects: QualityTrend[]; predictionStatus: string; }

export interface DecisionSupportRequest { weights?: Record<string, number>; minimumOccurrences?: number; }
export interface DecisionCriterion { id: string; name: string; weight: number; direction: string; source: string; }
export interface DecisionOptionEvidence {
  optionId: string; signature: string; category?: FailureCategory | null; normalizedMessage: string; sampleMessage: string;
  occurrencesInRun: number; recurrence30Days: number; affectedTestCases: number; triageSeverity?: string | null;
}
export interface DecisionContribution { criterionId: string; rawValue: number; normalizedValue: number; normalizedWeight: number; contribution: number; }
export interface DecisionRankedOption {
  rank: number; optionId: string; name: string; score: number; contributions: DecisionContribution[]; explanation: string; tiedWithOptionIds: string[];
}
export interface DecisionResult {
  evaluationId?: string | null; status: 'Ranked' | 'NoFeasibleOption' | string; question?: string | null; method: string;
  criteria: { id: string; name: string; direction: string; weight: number; normalizedWeight: number; minimumValue?: number | null; maximumValue?: number | null }[];
  ranking: DecisionRankedOption[]; infeasible: { optionId: string; name: string; violations: string[] }[]; notes: string[]; limitations: string;
}
/** A recorded SmartDog evaluation: submitted criteria and evidence, plus the result or the reason it failed. */
export interface DecisionEvaluation {
  id: string; testRunId: string; provider: string; status: AnalysisStatus; requestedAtUtc: string; completedAtUtc?: string | null;
  correlationId: string; question: string; criteria: DecisionCriterion[]; minimumOccurrences?: number | null;
  evidence: DecisionOptionEvidence[]; evidenceNotes: string[]; failureAnalysisId?: string | null; externalEvaluationId?: string | null;
  result?: DecisionResult | null; lastError?: string | null;
}
