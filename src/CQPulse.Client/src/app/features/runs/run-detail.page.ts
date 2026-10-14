import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { TEST_OUTCOMES, TestOutcome, isTerminal } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { Pager } from '../../shared/pager';

@Component({
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, DataState, Pager],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">RUN DETAIL</p>
        <h1>{{ run.value()?.runLabel || 'Run' }}</h1>
        @if (run.value(); as r) {
          <p class="intro"><span class="status" [attr.data-status]="r.status">{{ r.status }}</span> · requested {{ r.requestedAtUtc | date: 'medium' }}</p>
        }
      </div>
      <a routerLink="/runs">← Run history</a>
    </section>
    <app-data-state [loading]="run.loading()" [loaded]="run.loaded()" [error]="run.error()" (retry)="run.reload()" />
    @if (run.value(); as r) {
      <section class="metrics">
        <article><small>Completed</small><strong>{{ r.completedTests | number }}/{{ r.totalTests | number }}</strong><i>Tests processed</i></article>
        <article><small>Passed</small><strong>{{ r.passedCount | number }}</strong><i>Successful results</i></article>
        <article><small>Failed</small><strong>{{ r.failedCount | number }}</strong><i>Failures</i></article>
        <article><small>Skipped</small><strong>{{ r.skippedCount | number }}</strong><i>Skipped results</i></article>
      </section>
      @if (r.errorSummary) { <p class="error" role="alert">{{ r.errorSummary }}</p> }
      <section class="panel">
        <div class="panel-title">
          <div><p class="eyebrow">EXECUTION</p><h2>Configuration and timing</h2></div>
          @if (!isTerminal(r.status)) {
            <button type="button" class="secondary danger" [disabled]="cancelling()" (click)="cancel()">{{ cancelling() ? 'Cancelling…' : 'Cancel run' }}</button>
          }
        </div>
        @if (actionError()) { <p class="error" role="alert">{{ actionError() }}</p> }
        <dl class="definition-list">
          <dt>Started</dt><dd>{{ r.startedAtUtc ? (r.startedAtUtc | date: 'medium') : '—' }}</dd>
          <dt>Completed</dt><dd>{{ r.completedAtUtc ? (r.completedAtUtc | date: 'medium') : '—' }}</dd>
          <dt>Duration</dt><dd>{{ r.durationMs != null ? (r.durationMs / 1000 | number: '1.1-1') + ' s' : '—' }}</dd>
          <dt>Random seed</dt><dd>{{ r.randomSeed ?? 'Not set (random)' }}</dd>
          @if (r.cancellationReason) { <dt>Cancellation</dt><dd>{{ r.cancellationReason }}</dd> }
        </dl>
        <details><summary>Submitted configuration</summary><pre class="config">{{ formatConfig(r.configurationSnapshot) }}</pre></details>
      </section>
    }
    <section class="panel">
      <div class="panel-title">
        <div><p class="eyebrow">RESULTS</p><h2>Individual tests</h2></div>
        <select [(ngModel)]="outcome" (ngModelChange)="filterResults()" aria-label="Outcome filter">
          <option value="">All outcomes</option>
          @for (o of outcomes; track o) { <option [value]="o">{{ o }}</option> }
        </select>
      </div>
      <div class="table-row heading"><span>Test case</span><span>Outcome</span><span>Duration</span><span>Failure category</span></div>
      @for (result of results.value()?.items ?? []; track result.id) {
        <div class="table-row">
          <strong>{{ result.testCaseNameSnapshot }}<small>{{ result.errorMessage }}</small></strong>
          <span class="status" [attr.data-status]="result.outcome">{{ result.outcome }}</span>
          <span>{{ result.durationMs | number }} ms</span>
          <span>{{ result.failureCategory || '—' }}</span>
        </div>
      }
      <app-data-state [loading]="results.loading()" [loaded]="results.loaded()" [error]="results.error()"
        [empty]="!results.value()?.items?.length" emptyText="No persisted results match this outcome." (retry)="results.reload()" />
      <app-pager [page]="resultsPage" [pageSize]="resultsPageSize" [totalCount]="results.value()?.totalCount ?? 0" (pageChange)="goToResults($event)" />
    </section>
    <section class="panel">
      <div class="panel-title"><div><p class="eyebrow">EXECUTION LOGS</p><h2>Diagnostics</h2></div></div>
      @for (log of logs.value()?.items ?? []; track log.id) {
        <div class="log"><time>{{ log.timestampUtc | date: 'medium' }}</time><b>{{ log.level }}</b> {{ log.message }}</div>
      }
      <app-data-state [loading]="logs.loading()" [loaded]="logs.loaded()" [error]="logs.error()"
        [empty]="!logs.value()?.items?.length" emptyText="No execution logs were recorded for this run." (retry)="logs.reload()" />
    </section>`
})
export class RunDetailPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly runId = input.required<string>();
  protected readonly outcomes = TEST_OUTCOMES;
  protected readonly isTerminal = isTerminal;
  protected readonly resultsPageSize = 50;
  protected outcome: TestOutcome | '' = '';
  protected resultsPage = 1;
  protected readonly cancelling = signal(false);
  protected readonly actionError = signal('');
  protected readonly run = loadable(() => this.api.run(this.runId()));
  protected readonly results = loadable(() =>
    this.api.results(this.runId(), { outcome: this.outcome, page: this.resultsPage, pageSize: this.resultsPageSize }));
  protected readonly logs = loadable(() => this.api.logs(this.runId()));

  ngOnInit(): void {
    this.run.reload();
    this.results.reload();
    this.logs.reload();
  }

  protected filterResults(): void {
    this.resultsPage = 1;
    this.results.reload();
  }

  protected goToResults(page: number): void {
    this.resultsPage = page;
    this.results.reload();
  }

  protected cancel(): void {
    this.cancelling.set(true);
    this.actionError.set('');
    this.api.cancel(this.runId()).subscribe({
      next: () => { this.cancelling.set(false); this.run.reload(); this.results.reload(); },
      error: e => { this.cancelling.set(false); this.actionError.set(problemMessage(e, 'The run could not be cancelled.')); this.run.reload(); }
    });
  }

  protected formatConfig(snapshot: string): string {
    try { return JSON.stringify(JSON.parse(snapshot), null, 2); } catch { return snapshot; }
  }
}
