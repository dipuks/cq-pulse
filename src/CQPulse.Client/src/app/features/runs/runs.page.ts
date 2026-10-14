import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { utcRange } from '../../core/dates';
import { RUN_STATUSES, RunStatus } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { Pager } from '../../shared/pager';

@Component({
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, DataState, Pager],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">EXECUTION HISTORY</p>
        <h1>Test runs</h1>
        <p class="intro">Review persisted run outcomes, progress, and diagnostics.</p>
      </div>
      <a class="primary" routerLink="/simulator">Start run</a>
    </section>
    <section class="panel">
      <form class="filters" (ngSubmit)="applyFilters()">
        <label>Label<input [(ngModel)]="search" name="search" placeholder="Search run label"></label>
        <label>Project
          <select [(ngModel)]="project" name="project">
            <option value="">All projects</option>
            @for (p of projects.value()?.items ?? []; track p.id) { <option [value]="p.id">{{ p.name }}</option> }
          </select>
        </label>
        <label>Status
          <select [(ngModel)]="status" name="status">
            <option value="">All statuses</option>
            @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
          </select>
        </label>
        <label>Requested from (UTC)<input type="date" [(ngModel)]="fromDate" name="from"></label>
        <label>to (UTC, inclusive)<input type="date" [(ngModel)]="toDate" name="to"></label>
        <label>Sort
          <select [(ngModel)]="sort" name="sort">
            <option value="requested-desc">Newest first</option><option value="requested-asc">Oldest first</option>
            <option value="completed-desc">Recently completed</option>
          </select>
        </label>
        <button type="submit" class="secondary">Apply filters</button>
      </form>
      <div class="table-row heading"><span>Run</span><span>Status</span><span>Progress</span><span>Requested</span></div>
      @for (run of runs.value()?.items ?? []; track run.id) {
        <a class="table-row link-row" [routerLink]="['/runs', run.id]">
          <strong>{{ run.runLabel }}<small>{{ run.passedCount | number }} passed · {{ run.failedCount | number }} failed · {{ run.skippedCount | number }} skipped{{ run.rerunOfRunId ? ' · rerun' : '' }}</small></strong>
          <span class="status" [attr.data-status]="run.status">{{ run.status }}</span>
          <span>{{ run.completedTests | number }} / {{ run.totalTests | number }}</span>
          <span>{{ run.requestedAtUtc | date: 'medium' }}</span>
        </a>
      }
      <app-data-state [loading]="runs.loading()" [loaded]="runs.loaded()" [error]="runs.error()"
        [empty]="!runs.value()?.items?.length" emptyText="No executions match the current filters." (retry)="runs.reload()" />
      <app-pager [page]="page" [pageSize]="pageSize" [totalCount]="runs.value()?.totalCount ?? 0" (pageChange)="goTo($event)" />
    </section>`
})
export class RunsPage implements OnInit {
  private readonly api = inject(ApiService);
  /** Optional ?projectId= query parameter (e.g. from a project page). */
  readonly projectId = input<string>();
  protected readonly statuses = RUN_STATUSES;
  protected readonly pageSize = 25;
  protected search = '';
  protected project = '';
  protected status: RunStatus | '' = '';
  protected fromDate = '';
  protected toDate = '';
  protected sort = 'requested-desc';
  protected page = 1;
  protected readonly projects = loadable(() => this.api.projects({ pageSize: 100 }));
  protected readonly runs = loadable(() => this.api.runs({
    search: this.search.trim(), projectId: this.project, status: this.status, sort: this.sort,
    ...utcRange(this.fromDate, this.toDate), page: this.page, pageSize: this.pageSize
  }));

  ngOnInit(): void {
    this.project = this.projectId() ?? '';
    this.projects.reload();
    this.runs.reload();
  }

  protected applyFilters(): void { this.page = 1; this.runs.reload(); }
  protected goTo(page: number): void { this.page = page; this.runs.reload(); }
}
