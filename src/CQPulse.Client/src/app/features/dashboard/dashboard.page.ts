import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [RouterLink, DecimalPipe, DataState],
  template: `
    <section class="hero">
      <div>
        <p class="eyebrow">QUALITY COMMAND CENTER</p>
        <h1>See every run.<br><em>Know every risk.</em></h1>
        <p class="intro">Live operational data from your CQ Pulse workspace.</p>
      </div>
      <a class="primary" routerLink="/simulator">▶ Start simulation</a>
    </section>

    <app-data-state [loading]="summary.loading()" [loaded]="summary.loaded()" [error]="summary.error()" (retry)="summary.reload()" />
    @if (summary.value(); as s) {
      <section class="metrics">
        <article><small>Projects</small><strong>{{ s.projectCount | number }}</strong><i>{{ s.testSuiteCount | number }} suites · {{ s.testCaseCount | number }} test cases</i></article>
        <article><small>Test runs</small><strong>{{ s.totalRuns | number }}</strong><i>{{ s.activeRuns | number }} active now</i></article>
        <article><small>Passed results</small><strong>{{ s.passed | number }}</strong><i>{{ s.passRate }}% pass rate across all runs</i></article>
        <article><small>Failed results</small><strong>{{ s.failed | number }}</strong><i>{{ s.skipped | number }} skipped · need investigation</i></article>
      </section>
    }

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
        <div class="panel-title">
          <div><p class="eyebrow">PROJECTS</p><h2>Quality portfolio</h2></div>
          <a routerLink="/projects">View all →</a>
        </div>
        @for (project of projects.value()?.items ?? []; track project.id) {
          <div class="row">
            <a [routerLink]="['/projects', project.id, 'suites']">{{ project.name }}</a>
            <span class="status" [attr.data-status]="project.status">{{ project.status }}</span>
          </div>
        }
        <app-data-state [loading]="projects.loading()" [loaded]="projects.loaded()" [error]="projects.error()" [empty]="!projects.value()?.items?.length"
          emptyText="Create a project to begin." (retry)="projects.reload()" />
      </article>
    </section>`
})
export class DashboardPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly summary = loadable(() => this.api.dashboard());
  protected readonly runs = loadable(() => this.api.runs({ pageSize: 6 }));
  protected readonly projects = loadable(() => this.api.projects({ pageSize: 6 }));

  ngOnInit(): void {
    this.summary.reload();
    this.runs.reload();
    this.projects.reload();
  }
}
