import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin, map } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { utcDay, utcRange } from '../../core/dates';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { TrendChart } from '../../shared/trend-chart';

const TREND_DAYS = 30;

@Component({
  imports: [RouterLink, DecimalPipe, DataState, TrendChart],
  template: `
    <section class="hero">
      <div>
        <p class="eyebrow">QUALITY COMMAND CENTER</p>
        <h1>See every run.<br><em>Know every risk.</em></h1>
        <p class="intro">Live operational data from your CQ Pulse workspace.</p>
      </div>
      <a class="primary" routerLink="/simulator">▶ Configure simulation</a>
    </section>

    <app-data-state [loading]="summary.loading()" [loaded]="summary.loaded()" [error]="summary.error()" (retry)="summary.reload()" />
    @if (summary.value(); as s) {
      <section class="metrics" aria-label="Workspace totals">
        <article><small>Catalog</small><strong>{{ s.testCaseCount | number }}</strong><i>unique test cases · {{ s.testSuiteCount | number }} suites · {{ s.projectCount | number }} projects</i></article>
        <article><small>Test runs</small><strong>{{ s.totalRuns | number }}</strong><i>{{ s.activeRuns | number }} active now</i></article>
        <article><small>Pass rate</small><strong>{{ s.passRate }}%</strong><i>{{ s.passed | number }} passed of {{ s.passed + s.failed + s.skipped | number }} executed results</i></article>
        <article><small>Failed results</small><strong>{{ s.failed | number }}</strong><i>{{ s.skipped | number }} skipped · across all runs</i></article>
      </section>
    }

    <section class="grid">
      <article class="panel">
        <div class="panel-title">
          <div><p class="eyebrow">EXECUTION TRENDS</p><h2>Last {{ trendDays }} days</h2></div>
          <a routerLink="/analytics">Analytics →</a>
        </div>
        <app-trend-chart [points]="trends.value() ?? []" caption="Daily outcomes, last 30 UTC days" />
        <app-data-state [loading]="trends.loading()" [loaded]="trends.loaded()" [error]="trends.error()" [empty]="!trends.value()?.length"
          emptyText="No results in the last 30 days." (retry)="trends.reload()" />
      </article>
      <article class="panel">
        <div class="panel-title"><div><p class="eyebrow">FAILURE CATEGORIES</p><h2>Where failures cluster</h2></div></div>
        @for (f of failures.value() ?? []; track f.category) {
          <div class="failure-row">
            <span>{{ f.category }}</span>
            <div role="presentation"><i [style.width.%]="maxFailures() ? f.count / maxFailures() * 100 : 0"></i></div>
            <b>{{ f.count | number }}</b>
          </div>
        }
        <app-data-state [loading]="failures.loading()" [loaded]="failures.loaded()" [error]="failures.error()" [empty]="!failures.value()?.length"
          emptyText="No categorized failures in the last 30 days." (retry)="failures.reload()" />
      </article>
    </section>

    <section class="grid">
      <article class="panel">
        <div class="panel-title">
          <div><p class="eyebrow">RECENT EXECUTIONS</p><h2>Run activity</h2></div>
          <a routerLink="/runs">View all →</a>
        </div>
        @for (run of runs.value()?.items ?? []; track run.id) {
          <div class="row">
            <a [routerLink]="['/runs', run.id]">{{ run.runLabel }}</a>
            <span class="status" [attr.data-status]="run.status">{{ run.status }}</span>
            <span>{{ run.completedTests | number }} / {{ run.totalTests | number }}</span>
          </div>
        }
        <app-data-state [loading]="runs.loading()" [loaded]="runs.loaded()" [error]="runs.error()" [empty]="!runs.value()?.items?.length"
          emptyText="No runs have been recorded." (retry)="runs.reload()" />
      </article>
      <article class="panel">
        <div class="panel-title"><div><p class="eyebrow">ACTIVE NOW</p><h2>Queued and running</h2></div></div>
        @for (run of active.value() ?? []; track run.id) {
          <div class="row">
            <a [routerLink]="['/runs', run.id]">{{ run.runLabel }}</a>
            <span class="status" [attr.data-status]="run.status">{{ run.status }}</span>
            <span>{{ run.completedTests | number }} / {{ run.totalTests | number }}</span>
          </div>
        }
        <app-data-state [loading]="active.loading()" [loaded]="active.loaded()" [error]="active.error()" [empty]="!active.value()?.length"
          emptyText="No runs are queued or running." (retry)="active.reload()" />
      </article>
    </section>`
})
export class DashboardPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly trendDays = TREND_DAYS;
  private readonly window = utcRange(utcDay(-(TREND_DAYS - 1)), utcDay());
  protected readonly summary = loadable(() => this.api.dashboard());
  protected readonly runs = loadable(() => this.api.runs({ pageSize: 6 }));
  protected readonly trends = loadable(() => this.api.trends({ ...this.window }));
  protected readonly failures = loadable(() => this.api.failures({ ...this.window }));
  protected readonly active = loadable(() => forkJoin([
    this.api.runs({ status: 'Running', pageSize: 5 }),
    this.api.runs({ status: 'Queued', pageSize: 5 })
  ]).pipe(map(([running, queued]) => [...running.items, ...queued.items])));
  protected readonly maxFailures = computed(() => Math.max(0, ...(this.failures.value() ?? []).map(x => x.count)));

  ngOnInit(): void {
    for (const resource of [this.summary, this.runs, this.trends, this.failures, this.active]) resource.reload();
  }
}
