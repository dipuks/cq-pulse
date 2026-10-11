import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';

interface Project { id: string; name: string; description?: string; }
interface Run { id: string; status: string; totalTests: number; completedTests: number; passedCount: number; failedCount: number; skippedCount: number; }
interface DashboardSummary { projectCount: number; testSuiteCount: number; testCaseCount: number; totalRuns?: number; passed?: number; failed?: number; skipped?: number; activeRuns?: number; passRate: number; }

@Component({
  selector: 'app-root',
  imports: [FormsModule, NgFor, NgIf, DecimalPipe],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected readonly projects = signal<Project[]>([]);
  protected readonly selectedProjectId = signal('');
  protected readonly testCount = signal(100);
  protected readonly passPercentage = signal(88);
  protected readonly failurePercentage = signal(8);
  protected readonly skippedPercentage = signal(4);
  protected readonly isDark = signal(false);
  protected readonly isStarting = signal(false);
  protected readonly error = signal('');
  protected readonly activeRun = signal<Run | null>(null);
  protected readonly summary = signal<DashboardSummary | null>(null);
  protected readonly authenticated = signal(false);
  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly loginError = signal('');
  protected readonly loggingIn = signal(false);
  private readonly apiUrl = 'https://localhost:7256/api';
  private poller?: ReturnType<typeof setInterval>;

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    const savedTheme = localStorage.getItem('cq-pulse-theme');
    this.setTheme(savedTheme === 'dark' || (!savedTheme && matchMedia('(prefers-color-scheme: dark)').matches));
    this.authenticated.set(Boolean(localStorage.getItem('cq-pulse-token')));
    if (this.authenticated()) this.loadDashboard();
  }

  protected login(): void {
    this.loggingIn.set(true);
    this.loginError.set('');
    this.http.post<{ accessToken: string }>(`${this.apiUrl}/auth/login`, {
      email: this.email(), password: this.password()
    }).subscribe({
      next: response => {
        localStorage.setItem('cq-pulse-token', response.accessToken);
        this.password.set('');
        this.authenticated.set(true);
        this.loggingIn.set(false);
        this.loadDashboard();
      },
      error: () => {
        this.loggingIn.set(false);
        this.loginError.set('Sign in failed. Check the development administrator credentials.');
      }
    });
  }

  protected logout(): void {
    localStorage.removeItem('cq-pulse-token');
    this.authenticated.set(false);
  }

  private loadDashboard(): void {
    this.http.get<Project[]>(`${this.apiUrl}/projects`).subscribe({
      next: projects => {
        this.projects.set(projects);
        if (projects[0]) this.selectedProjectId.set(projects[0].id);
      },
      error: () => this.error.set('Unable to reach the CQ Pulse API. Start the API at https://localhost:7256.')
    });
    this.refreshSummary();
  }

  protected setTheme(dark: boolean): void {
    this.isDark.set(dark);
    document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
    localStorage.setItem('cq-pulse-theme', dark ? 'dark' : 'light');
  }

  protected startSimulation(): void {
    if (!this.selectedProjectId() || this.isStarting()) return;
    if (this.passPercentage() + this.failurePercentage() + this.skippedPercentage() !== 100) {
      this.error.set('Pass, failure, and skipped percentages must total 100%.');
      return;
    }
    this.isStarting.set(true);
    this.error.set('');
    this.http.post<{ runId: string }>(`${this.apiUrl}/simulator/runs`, {
      projectId: this.selectedProjectId(), testSuiteId: null, testCount: this.testCount(),
      maxConcurrency: 12, minimumDurationMs: 20, maximumDurationMs: 90,
      passPercentage: this.passPercentage(), failurePercentage: this.failurePercentage(), skippedPercentage: this.skippedPercentage(),
      failureCategories: ['UiElement', 'ApiError', 'Timeout', 'DataValidation', 'Environment'],
      runLabel: 'Dashboard simulation'
    }).subscribe({
      next: ({ runId }) => { this.isStarting.set(false); this.pollRun(runId); },
      error: () => { this.isStarting.set(false); this.error.set('The simulation could not be started. Check API availability and the selected project.'); }
    });
  }

  private pollRun(runId: string): void {
    if (this.poller) clearInterval(this.poller);
    const refresh = () => this.http.get<Run>(`${this.apiUrl}/simulator/runs/${runId}`).subscribe({
      next: run => {
        this.activeRun.set(run);
        this.refreshSummary();
        if (['Passed', 'CompletedWithFailures', 'Failed', 'Cancelled'].includes(run.status) && this.poller) {
          clearInterval(this.poller);
          this.poller = undefined;
        }
      }
    });
    refresh();
    this.poller = setInterval(refresh, 700);
  }

  private refreshSummary(): void {
    this.http.get<DashboardSummary>(`${this.apiUrl}/dashboard/summary`).subscribe({
      next: summary => this.summary.set(summary)
    });
  }
}
