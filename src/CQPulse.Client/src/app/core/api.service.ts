import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import {
  AnalyticsSummary, DashboardSummary, FailureCount, PageResponse, Project, ProjectSummary, RunDetail, RunLog, RunResult, RunSummary,
  SaveProject, SaveSuite, SaveTestCase, SimulationPreview, SimulationRequest, SimulatorLimits, Suite, TestCase, TestCaseExecution,
  TestOutcome, TrendPoint, FailureAnalysis, IntegrationStatus, AnomalyReport, FailureSignatureReport, FlakyReport, QualityTrendReport,
  DecisionEvaluation, DecisionSupportRequest
} from './models';

export type QueryParams = Record<string, string | number | boolean | null | undefined>;

/** Drops empty values so optional filters are simply omitted from the query string. */
export function toParams(query: QueryParams = {}): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly api = `${inject(API_BASE_URL)}/api`;

  projects(query: QueryParams = {}) { return this.http.get<PageResponse<Project>>(`${this.api}/projects`, { params: toParams(query) }); }
  project(id: string) { return this.http.get<Project>(`${this.api}/projects/${id}`); }
  projectSummary(id: string) { return this.http.get<ProjectSummary>(`${this.api}/projects/${id}/summary`); }
  saveProject(body: SaveProject, id?: string) {
    return id ? this.http.put<Project>(`${this.api}/projects/${id}`, body) : this.http.post<Project>(`${this.api}/projects`, body);
  }

  suites(projectId: string, query: QueryParams = {}) {
    return this.http.get<PageResponse<Suite>>(`${this.api}/projects/${projectId}/suites`, { params: toParams(query) });
  }
  suite(id: string) { return this.http.get<Suite>(`${this.api}/suites/${id}`); }
  saveSuite(projectId: string, body: SaveSuite, id?: string) {
    return id ? this.http.put<Suite>(`${this.api}/suites/${id}`, body) : this.http.post<Suite>(`${this.api}/projects/${projectId}/suites`, body);
  }

  cases(suiteId: string, query: QueryParams = {}) {
    return this.http.get<PageResponse<TestCase>>(`${this.api}/suites/${suiteId}/cases`, { params: toParams(query) });
  }
  testCase(id: string) { return this.http.get<TestCase>(`${this.api}/test-cases/${id}`); }
  setCaseEnabled(id: string, enabled: boolean) { return this.http.patch<TestCase>(`${this.api}/test-cases/${id}/enabled`, { enabled }); }
  deleteCase(id: string) { return this.http.delete<void>(`${this.api}/test-cases/${id}`); }
  caseHistory(id: string, query: QueryParams = {}) {
    return this.http.get<PageResponse<TestCaseExecution>>(`${this.api}/test-cases/${id}/results`, { params: toParams(query) });
  }
  saveCase(suiteId: string, body: SaveTestCase, id?: string) {
    return id ? this.http.put<TestCase>(`${this.api}/test-cases/${id}`, body) : this.http.post<TestCase>(`${this.api}/suites/${suiteId}/cases`, body);
  }

  dashboard() { return this.http.get<DashboardSummary>(`${this.api}/dashboard/summary`); }

  runs(query: QueryParams = {}) { return this.http.get<PageResponse<RunSummary>>(`${this.api}/runs`, { params: toParams(query) }); }
  run(id: string) { return this.http.get<RunDetail>(`${this.api}/runs/${id}`); }
  rerun(id: string) { return this.http.post<{ runId: string }>(`${this.api}/runs/${id}/rerun`, {}); }
  results(id: string, query: { outcome?: TestOutcome | ''; signature?: string; page?: number; pageSize?: number } = {}) {
    return this.http.get<PageResponse<RunResult>>(`${this.api}/runs/${id}/results`, { params: toParams(query) });
  }
  logs(id: string, query: QueryParams = {}) { return this.http.get<PageResponse<RunLog>>(`${this.api}/runs/${id}/logs`, { params: toParams(query) }); }

  simulatorLimits() { return this.http.get<SimulatorLimits>(`${this.api}/simulator/limits`); }
  preview(body: SimulationRequest) { return this.http.post<SimulationPreview>(`${this.api}/simulator/preview`, body); }
  start(body: SimulationRequest) { return this.http.post<{ runId: string }>(`${this.api}/simulator/runs`, body); }
  cancel(id: string) { return this.http.post<void>(`${this.api}/simulator/runs/${id}/cancel`, {}); }

  analyticsSummary(query: QueryParams = {}) { return this.http.get<AnalyticsSummary>(`${this.api}/analytics/summary`, { params: toParams(query) }); }
  trends(query: QueryParams = {}) { return this.http.get<TrendPoint[]>(`${this.api}/analytics/execution-trends`, { params: toParams(query) }); }
  failures(query: QueryParams = {}) { return this.http.get<FailureCount[]>(`${this.api}/analytics/failure-breakdown`, { params: toParams(query) }); }
  failureAnalyses(runId: string) { return this.http.get<FailureAnalysis[]>(`${this.api}/runs/${runId}/failure-analyses`); }
  requestFailureAnalysis(runId: string) { return this.http.post<FailureAnalysis>(`${this.api}/runs/${runId}/failure-analyses`, {}); }
  failureAnalysis(id: string) { return this.http.get<FailureAnalysis>(`${this.api}/failure-analyses/${id}`); }
  retryFailureAnalysis(id: string) { return this.http.post<FailureAnalysis>(`${this.api}/failure-analyses/${id}/retry`, {}); }
  integrationStatus() { return this.http.get<IntegrationStatus[]>(`${this.api}/integrations/status`); }

  decisionHistory(runId: string) { return this.http.get<DecisionEvaluation[]>(`${this.api}/runs/${runId}/decision-support`); }
  evaluateDecision(runId: string, body: DecisionSupportRequest) {
    return this.http.post<DecisionEvaluation>(`${this.api}/runs/${runId}/decision-support`, body);
  }

  failureSignatures(query: QueryParams = {}) { return this.http.get<FailureSignatureReport>(`${this.api}/insights/failure-signatures`, { params: toParams(query) }); }
  flakyTests(query: QueryParams = {}) { return this.http.get<FlakyReport>(`${this.api}/insights/flaky-tests`, { params: toParams(query) }); }
  anomalies(query: QueryParams = {}) { return this.http.get<AnomalyReport>(`${this.api}/insights/anomalies`, { params: toParams(query) }); }
  qualityTrends(query: QueryParams = {}) { return this.http.get<QualityTrendReport>(`${this.api}/insights/quality-trends`, { params: toParams(query) }); }
}
