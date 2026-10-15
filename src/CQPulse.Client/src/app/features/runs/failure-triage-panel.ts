import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnDestroy, computed, inject, input, signal, effect, untracked } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { FailureAnalysis } from '../../core/models';

/**
 * Failure triage for one run, backed by LogTriageAssistant through the CQ Pulse API. Shows the latest analysis exactly
 * as stored (lifecycle state, provider output, errors) and polls only while an analysis is pending or running.
 */
@Component({
  selector: 'app-failure-triage',
  imports: [DatePipe, DecimalPipe],
  template: `
    <section class="panel" aria-live="polite">
      <div class="panel-title">
        <div>
          <p class="eyebrow">FAILURE TRIAGE · LOGTRIAGEASSISTANT</p>
          <h2>Diagnostic analysis</h2>
        </div>
        @if (!latest() && canRequest()) {
          <button type="button" class="secondary" [disabled]="busy()" (click)="request()">{{ busy() ? 'Requesting…' : 'Analyze failures' }}</button>
        }
        @if (latest(); as a) {
          @if (a.status === 'Failed' || a.status === 'Unavailable' || a.status === 'Succeeded') {
            <button type="button" class="secondary" [disabled]="busy()" (click)="retry(a.id)"
              title="Re-submit this run's stored failures; the run is not executed again">{{ busy() ? 'Queueing…' : '↻ Re-analyze' }}</button>
          }
        }
      </div>
      @if (error()) { <p class="error" role="alert">{{ error() }}</p> }

      @if (latest(); as a) {
        <p>
          <span class="status" [attr.data-status]="statusBadge(a.status)">{{ a.status }}</span>
          <span class="empty"> · requested {{ a.requestedAtUtc | date: 'medium' }}{{ a.completedAtUtc ? ' · finished ' + (a.completedAtUtc | date: 'mediumTime') : '' }}
            · attempt {{ a.attempts }} · {{ a.inputFailureCount | number }} failures submitted{{ a.inputTruncated ? ' (truncated)' : '' }}</span>
        </p>
        @if (a.waitingForQueueSlot) {
          <p class="empty">Waiting for a free analysis slot: the queue is full, and this analysis is queued automatically as earlier ones finish.</p>
        } @else if (a.status === 'Pending' || a.status === 'Running') { <p class="empty">Analysis in progress…</p> }
        @if (a.lastError) { <p class="error">{{ a.lastError }}</p> }
        @if (a.status === 'Succeeded') {
          <dl class="definition-list">
            <dt>Severity</dt><dd><span class="status" [attr.data-status]="severityBadge(a.severity)">{{ a.severity }}</span></dd>
            <dt>Signal score</dt><dd>{{ a.signalScore ?? '—' }} <small class="empty">(LogTriageAssistant heuristic, 0–100; not a probability)</small></dd>
            <dt>Top category</dt><dd>{{ a.topCategory ?? '—' }} <small class="empty">(LogTriageAssistant's keyword category)</small></dd>
            <dt>Summary</dt><dd>{{ a.summary }}</dd>
          </dl>
          @if (a.recommendations.length) {
            <h3>Suggested next steps</h3>
            <ol>@for (r of a.recommendations; track $index) { <li>{{ r }}</li> }</ol>
          }
          <h3>Findings ({{ a.findings.length | number }}{{ a.findings.length >= 100 ? ', first 100 stored' : '' }})</h3>
          @for (f of a.findings.slice(0, shown()); track $index) {
            <div class="log">
              <b>{{ f.severity }}</b> <span class="status">{{ f.category }}</span>
              <div><code>{{ f.evidence }}</code></div>
              <small class="empty">{{ f.suggestion }}</small>
            </div>
          }
          @if (a.findings.length > shown()) {
            <button type="button" class="secondary" (click)="shown.set(shown() + 10)">Show more findings</button>
          }
        }
        <p class="empty"><small>Correlation ID {{ a.correlationId }}{{ a.externalAnalysisId ? ' · LogTriageAssistant analysis ' + a.externalAnalysisId : '' }}</small></p>
      } @else if (loaded()) {
        <p class="empty">{{ canRequest() ? 'No analysis yet. Submit this run’s persisted failures to LogTriageAssistant.' : failedCount() === 0 ? 'This run has no failed results to analyze.' : 'Available once the run has finished.' }}</p>
      }
    </section>`
})
export class FailureTriagePanel implements OnDestroy {
  private readonly api = inject(ApiService);
  readonly runId = input.required<string>();
  readonly runFinished = input(false);
  readonly failedCount = input(0);

  protected readonly analyses = signal<FailureAnalysis[]>([]);
  protected readonly latest = computed(() => this.analyses()[0] ?? null);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly shown = signal(10);
  protected readonly canRequest = computed(() => this.runFinished() && this.failedCount() > 0);
  private timer?: ReturnType<typeof setTimeout>;

  constructor() {
    effect(() => {
      const runId = this.runId();
      this.runFinished(); // reload when the run finishes (auto-analysis may have been queued)
      untracked(() => this.load(runId));
    });
  }

  ngOnDestroy(): void { clearTimeout(this.timer); }

  protected request(): void {
    this.busy.set(true);
    this.error.set('');
    this.api.requestFailureAnalysis(this.runId()).subscribe({
      next: () => { this.busy.set(false); this.load(this.runId()); },
      error: e => { this.busy.set(false); this.error.set(problemMessage(e, 'Analysis could not be requested.')); }
    });
  }

  protected retry(analysisId: string): void {
    this.busy.set(true);
    this.error.set('');
    this.api.retryFailureAnalysis(analysisId).subscribe({
      next: () => { this.busy.set(false); this.load(this.runId()); },
      error: e => { this.busy.set(false); this.error.set(problemMessage(e, 'Analysis could not be retried.')); }
    });
  }

  protected statusBadge(status: string): string {
    return status === 'Succeeded' ? 'Passed' : status === 'Pending' || status === 'Running' ? 'Running' : status === 'Unavailable' ? 'Skipped' : 'Failed';
  }

  protected severityBadge(severity?: string | null): string {
    return severity === 'CRITICAL' || severity === 'HIGH' ? 'Failed' : severity === 'MEDIUM' ? 'Skipped' : 'Passed';
  }

  private load(runId: string): void {
    clearTimeout(this.timer);
    this.api.failureAnalyses(runId).subscribe({
      next: list => {
        this.analyses.set(list);
        this.loaded.set(true);
        const active = list[0] && (list[0].status === 'Pending' || list[0].status === 'Running');
        if (active) this.timer = setTimeout(() => this.load(runId), 1_500);
      },
      error: e => { this.loaded.set(true); this.error.set(problemMessage(e, 'Analyses could not be loaded.')); }
    });
  }
}
