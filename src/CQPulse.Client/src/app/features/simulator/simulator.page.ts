import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { API_BASE_URL } from '../../core/api-config';
import { ApiService } from '../../core/api.service';
import { AuthService } from '../../core/auth';
import { problemMessage } from '../../core/errors';
import {
  FAILURE_CATEGORIES, FailureCategory, Project, RunDetail, RunProgress, SimulationPreview, SimulationRequest, SimulatorLimits, Suite, isTerminal
} from '../../core/models';
import { RunLiveConnection } from './run-live-connection';

export const defaultRequest = (): SimulationRequest => ({
  projectId: '', testSuiteId: null, testCount: 100, maxConcurrency: 12, minimumDurationMs: 20, maximumDurationMs: 90,
  passPercentage: 88, failurePercentage: 8, skippedPercentage: 4, failureCategories: [...FAILURE_CATEGORIES],
  randomSeed: null, runLabel: 'Simulator run', workloadSource: 'Catalog'
});

/** Client-side checks mirroring the server validator, using the limits the API reports. */
export function validateRequest(request: SimulationRequest, limits: SimulatorLimits | null): string[] {
  const errors: string[] = [];
  const whole = (n: unknown) => typeof n === 'number' && Number.isInteger(n);
  if (!request.projectId) errors.push('Choose a project.');
  if (limits && (!whole(request.testCount) || request.testCount < 1 || request.testCount > limits.maxTestCount))
    errors.push(`Test count must be between 1 and ${limits.maxTestCount.toLocaleString()}.`);
  if (limits && (!whole(request.maxConcurrency) || request.maxConcurrency < 1 || request.maxConcurrency > limits.maxConcurrency))
    errors.push(`Concurrency must be between 1 and ${limits.maxConcurrency}.`);
  if (!whole(request.minimumDurationMs) || request.minimumDurationMs < 0) errors.push('Minimum duration must be 0 or more.');
  if (!whole(request.maximumDurationMs) || request.maximumDurationMs < request.minimumDurationMs)
    errors.push('Maximum duration must be at least the minimum duration.');
  if (limits && request.maximumDurationMs > limits.maxDurationMs)
    errors.push(`Maximum duration must not exceed ${limits.maxDurationMs.toLocaleString()} ms.`);
  const percentages = [request.passPercentage, request.failurePercentage, request.skippedPercentage];
  if (percentages.some(p => !whole(p) || p < 0 || p > 100)) errors.push('Percentages must be whole numbers from 0 to 100.');
  else if (percentages.reduce((a, b) => a + b, 0) !== 100) errors.push('Pass, failure and skipped percentages must total 100.');
  if (request.failurePercentage > 0 && request.failureCategories.length === 0) errors.push('Select at least one failure category.');
  if (request.randomSeed !== null && !whole(request.randomSeed)) errors.push('Random seed must be a whole number.');
  return errors;
}

@Component({
  imports: [FormsModule, RouterLink, DecimalPipe, DatePipe],
  templateUrl: './simulator.page.html'
})
export class SimulatorPage implements OnInit, OnDestroy {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly live = new RunLiveConnection(`${inject(API_BASE_URL)}/hubs/runs`, () => this.auth.token());
  private poller?: ReturnType<typeof setInterval>;
  private pollTick = 0;
  private refreshSubscription?: Subscription;

  protected readonly categories = FAILURE_CATEGORIES;
  protected model = defaultRequest();
  protected readonly projects = signal<Project[]>([]);
  protected readonly suites = signal<Suite[]>([]);
  protected readonly limits = signal<SimulatorLimits | null>(null);
  protected readonly loadError = signal('');
  protected readonly errors = signal<string[]>([]);
  protected readonly preview = signal<SimulationPreview | null>(null);
  protected readonly previewing = signal(false);
  protected readonly starting = signal(false);
  protected readonly activeRun = signal<RunDetail | null>(null);
  protected readonly actionError = signal('');
  protected readonly now = signal(Date.now());
  protected readonly liveState = this.live.state;
  protected readonly liveDetail = this.live.detail;
  protected readonly isTerminal = isTerminal;

  protected readonly runActive = computed(() => { const run = this.activeRun(); return !!run && !isTerminal(run.status); });
  protected readonly elapsedSeconds = computed(() => {
    const run = this.activeRun();
    if (!run?.startedAtUtc) return 0;
    const end = run.completedAtUtc ? Date.parse(run.completedAtUtc) : this.now();
    return Math.max(0, (end - Date.parse(run.startedAtUtc)) / 1000);
  });
  protected readonly throughput = computed(() => {
    const seconds = this.elapsedSeconds();
    return seconds > 0 ? (this.activeRun()?.completedTests ?? 0) / seconds : 0;
  });

  ngOnInit(): void {
    this.api.simulatorLimits().subscribe({ next: limits => this.limits.set(limits), error: e => this.loadError.set(problemMessage(e)) });
    this.api.projects({ pageSize: 100 }).subscribe({
      next: page => {
        this.projects.set(page.items);
        if (page.items.length && !this.model.projectId) {
          this.model.projectId = page.items[0].id;
          this.loadSuites();
        }
      },
      error: e => this.loadError.set(problemMessage(e, 'Projects could not be loaded.'))
    });
  }

  ngOnDestroy(): void {
    this.stopWatching();
  }

  protected loadSuites(): void {
    this.model.testSuiteId = null;
    this.suites.set([]);
    this.invalidatePreview();
    if (this.model.projectId) this.api.suites(this.model.projectId, { pageSize: 100 }).subscribe({ next: page => this.suites.set(page.items), error: () => this.suites.set([]) });
  }

  /** Any edit makes the previous preview stale; the run can only be queued from a current preview. */
  protected invalidatePreview(): void {
    this.preview.set(null);
    this.actionError.set('');
  }

  protected toggleCategory(category: FailureCategory, checked: boolean): void {
    this.model.failureCategories = checked
      ? [...this.model.failureCategories, category]
      : this.model.failureCategories.filter(x => x !== category);
    this.invalidatePreview();
  }

  protected requestPreview(): void {
    const errors = validateRequest(this.model, this.limits());
    this.errors.set(errors);
    if (errors.length) return;
    this.previewing.set(true);
    this.api.preview(this.model).subscribe({
      next: preview => { this.previewing.set(false); this.preview.set(preview); },
      error: e => { this.previewing.set(false); this.errors.set([problemMessage(e, 'The preview was rejected.')]); }
    });
  }

  protected start(): void {
    if (!this.preview() || this.starting() || this.runActive()) return;
    this.starting.set(true);
    this.actionError.set('');
    this.api.start(this.model).subscribe({
      next: ({ runId }) => { this.starting.set(false); this.preview.set(null); this.watch(runId); },
      error: e => { this.starting.set(false); this.actionError.set(problemMessage(e, 'The simulator could not queue this run.')); }
    });
  }

  protected cancel(): void {
    const run = this.activeRun();
    if (!run) return;
    this.api.cancel(run.id).subscribe({
      next: () => this.refresh(run.id),
      error: e => { this.actionError.set(problemMessage(e, 'The run could not be cancelled.')); this.refresh(run.id); }
    });
  }

  private watch(runId: string): void {
    this.stopWatching();
    this.refresh(runId);
    this.pollTick = 0;
    // REST polling is the source of truth and the fallback; it slows down while live updates are healthy.
    this.poller = setInterval(() => {
      this.now.set(Date.now());
      this.pollTick++;
      if (this.liveState() !== 'connected' || this.pollTick % 5 === 0) this.refresh(runId);
    }, 1_000);
    void this.live.watch(runId, progress => this.applyProgress(progress), () => this.refresh(runId));
  }

  private refresh(runId: string): void {
    this.refreshSubscription?.unsubscribe();
    this.refreshSubscription = this.api.run(runId).subscribe({
      next: run => {
        this.activeRun.set(run);
        if (isTerminal(run.status)) this.finish();
      },
      error: e => this.actionError.set(problemMessage(e, 'Run status could not be refreshed.'))
    });
  }

  private applyProgress(progress: RunProgress): void {
    const run = this.activeRun();
    if (!run || run.id !== progress.runId || isTerminal(run.status)) return;
    // Live events can arrive out of order with REST responses; never move progress backwards.
    if (progress.completedTests < run.completedTests && !isTerminal(progress.status)) return;
    this.activeRun.set({ ...run, ...progress, id: run.id });
    if (isTerminal(progress.status)) this.refresh(run.id); // fetch authoritative final timestamps and summary
  }

  private finish(): void {
    if (this.poller) clearInterval(this.poller);
    this.poller = undefined;
    this.now.set(Date.now());
    void this.live.stop();
  }

  private stopWatching(): void {
    if (this.poller) clearInterval(this.poller);
    this.poller = undefined;
    this.refreshSubscription?.unsubscribe();
    void this.live.stop();
  }
}
