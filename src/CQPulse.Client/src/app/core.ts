import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

export interface PageResponse<T> { items: T[]; totalCount: number; page: number; pageSize: number; }
export interface Project { id: string; name: string; description?: string; status: string; createdAtUtc: string; }
export interface Suite { id: string; projectId: string; name: string; description?: string; testCaseCount: number; }
export interface TestCase { id: string; testSuiteId: string; name: string; description?: string; enabled: boolean; priority: string; simulatedOutcome: string; simulatedDurationMs: number; }
export interface Run { id: string; projectId: string; testSuiteId?: string; runLabel: string; status: string; requestedAtUtc: string; startedAtUtc?: string; completedAtUtc?: string; totalTests: number; completedTests: number; passedCount: number; failedCount: number; skippedCount: number; errorSummary?: string; cancellationReason?: string; durationMs?: number; configurationSnapshot?: string; }
export interface RunResult { id: string; testCaseNameSnapshot: string; outcome: string; durationMs: number; completedAtUtc: string; errorMessage?: string; failureCategory?: string; }
export interface RunLog { id: string; timestampUtc: string; level: string; message: string; metadataJson?: string; }
export interface Summary { projectCount: number; testSuiteCount: number; testCaseCount: number; totalRuns: number; passed: number; failed: number; skipped: number; activeRuns: number; passRate: number; }
export interface AnalyticsSummary { total: number; passed: number; failed: number; skipped: number; passRate: number; failureRate: number; averageDurationMs?: number; }
export interface Trend { date: string; passed: number; failed: number; skipped: number; averageDurationMs: number; }
export interface Failure { category: string; count: number; }
export interface SimulationRequest { projectId: string; testSuiteId: string | null; testCount: number; maxConcurrency: number; minimumDurationMs: number; maximumDurationMs: number; passPercentage: number; failurePercentage: number; skippedPercentage: number; failureCategories: string[]; randomSeed: number | null; runLabel: string; }
export interface Preview { testCount: number; expectedPassed: number; expectedFailed: number; expectedSkipped: number; maxConcurrency: number; resourceWarning: string; }
const API = 'https://localhost:7256/api';

@Injectable({ providedIn: 'root' }) export class ApiService {
  constructor(private http: HttpClient) {}
  projects(search = '') { return this.http.get<PageResponse<Project>>(`${API}/projects`, { params: search ? { search } : {} }); }
  project(id: string) { return this.http.get<Project>(`${API}/projects/${id}`); }
  saveProject(body: Partial<Project>, id?: string) { return id ? this.http.put<Project>(`${API}/projects/${id}`, body) : this.http.post<Project>(`${API}/projects`, body); }
  suites(projectId: string) { return this.http.get<PageResponse<Suite>>(`${API}/projects/${projectId}/suites`); }
  suite(id: string) { return this.http.get<Suite>(`${API}/suites/${id}`); }
  saveSuite(projectId: string, body: Partial<Suite>, id?: string) { return id ? this.http.put<Suite>(`${API}/suites/${id}`, body) : this.http.post<Suite>(`${API}/projects/${projectId}/suites`, body); }
  cases(suiteId: string, search = '') { return this.http.get<PageResponse<TestCase>>(`${API}/suites/${suiteId}/cases`, { params: search ? { search } : {} }); }
  testCase(id: string) { return this.http.get<TestCase>(`${API}/test-cases/${id}`); }
  saveCase(suiteId: string, body: Partial<TestCase>, id?: string) { return id ? this.http.put<TestCase>(`${API}/test-cases/${id}`, body) : this.http.post<TestCase>(`${API}/suites/${suiteId}/cases`, body); }
  dashboard() { return this.http.get<Summary>(`${API}/dashboard/summary`); }
  runs(params: Record<string, string> = {}) { return this.http.get<PageResponse<Run>>(`${API}/runs`, { params }); }
  run(id: string) { return this.http.get<Run>(`${API}/runs/${id}`); }
  results(id: string, outcome = '') { return this.http.get<PageResponse<RunResult>>(`${API}/runs/${id}/results`, { params: outcome ? { outcome } : {} }); }
  logs(id: string) { return this.http.get<PageResponse<RunLog>>(`${API}/runs/${id}/logs`); }
  preview(body: SimulationRequest) { return this.http.post<Preview>(`${API}/simulator/preview`, body); }
  start(body: SimulationRequest) { return this.http.post<{ runId: string }>(`${API}/simulator/runs`, body); }
  simulatorRun(id: string) { return this.http.get<Run>(`${API}/simulator/runs/${id}`); }
  cancel(id: string) { return this.http.post<void>(`${API}/simulator/runs/${id}/cancel`, {}); }
  analyticsSummary() { return this.http.get<AnalyticsSummary>(`${API}/analytics/summary`); }
  trends() { return this.http.get<Trend[]>(`${API}/analytics/execution-trends`); }
  failures() { return this.http.get<Failure[]>(`${API}/analytics/failure-breakdown`); }
}
@Injectable({ providedIn: 'root' }) export class AuthService {
  readonly error = signal('');
  constructor(private http: HttpClient, private router: Router) {}
  isAuthenticated(): boolean { return Boolean(localStorage.getItem('cq-pulse-token')); }
  login(email: string, password: string) { return this.http.post<{ accessToken: string }>(`${API}/auth/login`, { email, password }); }
  signOut(): void { localStorage.removeItem('cq-pulse-token'); void this.router.navigateByUrl('/login'); }
}
@Injectable({ providedIn: 'root' }) export class ThemeService {
  initialize(): boolean {
    const savedTheme = localStorage.getItem('cq-pulse-theme');
    const dark = savedTheme === 'dark' || (!savedTheme && typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches);
    return this.set(dark);
  }
  isDark(): boolean { return document.documentElement.dataset['theme'] === 'dark'; }
  set(dark: boolean): boolean { document.documentElement.dataset['theme'] = dark ? 'dark' : 'light'; localStorage.setItem('cq-pulse-theme', dark ? 'dark' : 'light'); return dark; }
}
export const authGuard: CanActivateFn = () => Boolean(localStorage.getItem('cq-pulse-token')) || inject(Router).createUrlTree(['/login']);
