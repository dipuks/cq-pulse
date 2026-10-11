import { HttpClient } from '@angular/common/http';
import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe, NgFor, NgIf } from '@angular/common';

interface Project { id: string; name: string; description?: string; }
interface Run { id: string; status: string; totalTests: number; completedTests: number; passedCount: number; failedCount: number; skippedCount: number; }

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
  private readonly apiUrl = 'https://localhost:7256/api';
  private poller?: ReturnType<typeof setInterval>;

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    const savedTheme = localStorage.getItem('cq-pulse-theme');
    this.setTheme(savedTheme === 'dark' || (!savedTheme && matchMedia('(prefers-color-scheme: dark)').matches));
    this.http.get<Project[]>(`${this.apiUrl}/projects`).subscribe({
      next: projects => {
        this.projects.set(projects);
        if (projects[0]) this.selectedProjectId.set(projects[0].id);
      },
      error: () => this.error.set('Unable to reach the CQ Pulse API. Start the API at https://localhost:7256.')
    });
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
        if (['Passed', 'CompletedWithFailures', 'Failed', 'Cancelled'].includes(run.status) && this.poller) {
          clearInterval(this.poller);
          this.poller = undefined;
        }
      }
    });
    refresh();
    this.poller = setInterval(refresh, 700);
  }
}
