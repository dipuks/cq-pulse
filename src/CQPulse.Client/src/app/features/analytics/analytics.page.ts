import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/api.service';
import { utcRange } from '../../core/dates';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { TrendChart } from '../../shared/trend-chart';

@Component({
  imports: [FormsModule, DatePipe, DecimalPipe, DataState, TrendChart],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">PERSISTED ANALYTICS</p>
        <h1>Quality analytics</h1>
        <p class="intro">Aggregates of stored test results (one per executed test, across runs). Dates are UTC calendar days; the end date is inclusive.</p>
      </div>
    </section>
    <form class="panel filters" (ngSubmit)="apply()" aria-label="Analytics filters">
      <label>Project
        <select [(ngModel)]="projectId" name="project">
          <option value="">All projects</option>
          @for (p of projects.value()?.items ?? []; track p.id) { <option [value]="p.id">{{ p.name }}</option> }
        </select>
      </label>
      <label>From (UTC)<input type="date" [(ngModel)]="fromDate" name="from"></label>
      <label>To (UTC, inclusive)<input type="date" [(ngModel)]="toDate" name="to"></label>
      <button type="submit" class="secondary">Apply</button>
      <button type="button" class="secondary" (click)="clear()">Clear</button>
    </form>

    <app-data-state [loading]="summary.loading()" [loaded]="summary.loaded()" [error]="summary.error()"
      [empty]="summary.value()?.total === 0" emptyText="No persisted results match these filters." (retry)="summary.reload()" />
    @if (summary.value(); as s) {
      @if (s.total > 0) {
        <section class="metrics">
          <article><small>Total results</small><strong>{{ s.total | number }}</strong><i>Executed tests</i></article>
          <article><small>Pass rate</small><strong>{{ s.passRate }}%</strong><i>{{ s.passed | number }} passed</i></article>
          <article><small>Failure rate</small><strong>{{ s.failureRate }}%</strong><i>{{ s.failed | number }} failed · {{ s.skipped | number }} skipped</i></article>
          <article><small>Average duration</small><strong>{{ (s.averageDurationMs ?? 0) | number: '1.0-0' }} ms</strong><i>Per test</i></article>
        </section>
      }
    }
    <section class="grid">
      <article class="panel">
        <p class="eyebrow">EXECUTION TRENDS</p><h2>Daily outcomes (UTC)</h2>
        <app-trend-chart [points]="trends.value() ?? []" caption="Daily outcomes for the selected filters" />
        <div class="table-row heading"><span>Date</span><span>Passed</span><span>Failed</span><span>Skipped</span></div>
        @for (t of trends.value() ?? []; track t.date) {
          <div class="table-row">
            <span>{{ t.date | date: 'mediumDate' }}</span><span>{{ t.passed | number }}</span><span>{{ t.failed | number }}</span><span>{{ t.skipped | number }}</span>
          </div>
        }
        <app-data-state [loading]="trends.loading()" [loaded]="trends.loaded()" [error]="trends.error()"
          [empty]="!trends.value()?.length" emptyText="No results in this range." (retry)="trends.reload()" />
      </article>
      <article class="panel">
        <p class="eyebrow">FAILURE BREAKDOWN</p><h2>Categories</h2>
        @for (f of failures.value() ?? []; track f.category) {
          <div class="failure-row">
            <span>{{ f.category }}</span>
            <div role="presentation"><i [style.width.%]="maxFailures() ? f.count / maxFailures() * 100 : 0"></i></div>
            <b>{{ f.count | number }}</b>
          </div>
        }
        <app-data-state [loading]="failures.loading()" [loaded]="failures.loaded()" [error]="failures.error()"
          [empty]="!failures.value()?.length" emptyText="No categorized failures in this range." (retry)="failures.reload()" />
      </article>
    </section>`
})
export class AnalyticsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected projectId = '';
  protected fromDate = '';
  protected toDate = '';
  private readonly query = () => ({ projectId: this.projectId, ...utcRange(this.fromDate, this.toDate) });
  protected readonly projects = loadable(() => this.api.projects({ pageSize: 100 }));
  protected readonly summary = loadable(() => this.api.analyticsSummary(this.query()));
  protected readonly trends = loadable(() => this.api.trends(this.query()));
  protected readonly failures = loadable(() => this.api.failures(this.query()));
  protected readonly maxFailures = computed(() => Math.max(0, ...(this.failures.value() ?? []).map(x => x.count)));

  ngOnInit(): void {
    this.projects.reload();
    this.apply();
  }

  protected apply(): void {
    this.summary.reload();
    this.trends.reload();
    this.failures.reload();
  }

  protected clear(): void {
    this.projectId = '';
    this.fromDate = '';
    this.toDate = '';
    this.apply();
  }
}
