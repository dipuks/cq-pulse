import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { SaveProject } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, DataState],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">PROJECT</p>
        <h1>{{ summary.value()?.name ?? 'Project' }}</h1>
        @if (summary.value(); as s) {
          <p class="intro"><span class="status" [attr.data-status]="s.status">{{ s.status }}</span> · created {{ s.createdAtUtc | date: 'mediumDate' }}{{ s.description ? ' · ' + s.description : '' }}</p>
        }
      </div>
      <div class="actions">
        <a routerLink="/projects">← Projects</a>
        <a class="primary" [routerLink]="['/projects', projectId(), 'suites']">Suites and test cases</a>
      </div>
    </section>
    <app-data-state [loading]="summary.loading()" [loaded]="summary.loaded()" [error]="summary.error()" (retry)="summary.reload()" />
    @if (summary.value(); as s) {
      <section class="metrics">
        <article><small>Catalog</small><strong>{{ s.testCaseCount | number }}</strong><i>test cases ({{ s.enabledTestCaseCount | number }} enabled) in {{ s.suiteCount | number }} suites</i></article>
        <article><small>Runs</small><strong>{{ s.runCount | number }}</strong><i>{{ s.activeRunCount | number }} active now</i></article>
        <article><small>Pass rate</small><strong>{{ s.passRate }}%</strong><i>{{ s.passedResults | number }} passed results</i></article>
        <article><small>Failed results</small><strong>{{ s.failedResults | number }}</strong><i>{{ s.skippedResults | number }} skipped · all runs</i></article>
      </section>
      <section class="split">
        <article class="panel">
          <div class="panel-title">
            <div><p class="eyebrow">RECENT RUNS</p><h2>Latest executions</h2></div>
            <a [routerLink]="['/runs']" [queryParams]="{ projectId: s.id }">All runs →</a>
          </div>
          @for (run of s.recentRuns; track run.id) {
            <div class="row">
              <a [routerLink]="['/runs', run.id]">{{ run.runLabel }}</a>
              <span class="status" [attr.data-status]="run.status">{{ run.status }}</span>
              <span>{{ run.requestedAtUtc | date: 'short' }}</span>
            </div>
          } @empty {
            <p class="empty">No runs yet. <a routerLink="/simulator">Configure a simulation →</a></p>
          }
        </article>
        <form class="panel form" (ngSubmit)="save()">
          <h2>Project details</h2>
          <label>Name<input [(ngModel)]="model.name" name="name" required maxlength="200"></label>
          <label>Description<textarea [(ngModel)]="model.description" name="description" maxlength="2000"></textarea></label>
          <label>Status<select [(ngModel)]="model.status" name="status"><option>Active</option><option>Archived</option></select></label>
          @if (message()) { <p [class]="saved() ? 'empty' : 'error'" role="status">{{ message() }}</p> }
          <button class="primary" type="submit" [disabled]="saving() || !model.name.trim()">{{ saving() ? 'Saving…' : 'Save details' }}</button>
        </form>
      </section>
    }`
})
export class ProjectDetailPage implements OnInit {
  private readonly api = inject(ApiService);
  readonly projectId = input.required<string>();
  protected readonly summary = loadable(() => this.api.projectSummary(this.projectId()));
  protected model: SaveProject = { name: '', description: '', status: 'Active' };
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly message = signal('');

  constructor() {
    effect(() => {
      const s = this.summary.value();
      if (s) this.model = { name: s.name, description: s.description ?? '', status: s.status };
    });
  }

  ngOnInit(): void { this.summary.reload(); }

  protected save(): void {
    this.saving.set(true);
    this.message.set('');
    this.api.saveProject(this.model, this.projectId()).subscribe({
      next: () => { this.saving.set(false); this.saved.set(true); this.message.set('Project saved.'); this.summary.reload(); },
      error: e => { this.saving.set(false); this.saved.set(false); this.message.set(problemMessage(e, 'Project could not be saved.')); }
    });
  }
}
