import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { PRIORITIES, SIMULATED_OUTCOMES, TEST_OUTCOMES, TestCase, TestOutcome } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { Pager } from '../../shared/pager';

@Component({
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, DataState, Pager],
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">TEST CASE</p><h1>{{ model()?.name || 'Test case' }}</h1></div>
      @if (model(); as m) { <a [routerLink]="['/suites', m.testSuiteId, 'cases']">← Suite test cases</a> }
    </section>
    <app-data-state [loading]="loading()" [loaded]="!loading()" [error]="loadError()" (retry)="load()" />
    @if (model(); as m) {
      <section class="split">
        <form class="panel form" (ngSubmit)="save(m)">
          <h2>Configuration</h2>
          <label>Name<input [(ngModel)]="m.name" name="name" required maxlength="200"></label>
          <label>Description<textarea [(ngModel)]="m.description" name="description" maxlength="4000"></textarea></label>
          <div class="two-col">
            <label>Priority
              <select [(ngModel)]="m.priority" name="priority">@for (p of priorities; track p) { <option [value]="p">{{ p }}</option> }</select>
            </label>
            <label>Simulated outcome
              <select [(ngModel)]="m.simulatedOutcome" name="outcome">@for (o of outcomes; track o) { <option [value]="o">{{ o }}</option> }</select>
            </label>
          </div>
          <label>Duration (ms)<input type="number" [(ngModel)]="m.simulatedDurationMs" name="duration" min="0" max="60000"></label>
          <label class="check"><input type="checkbox" [(ngModel)]="m.enabled" name="enabled"> Enabled for future catalog runs</label>
          @if (saveError()) { <p class="error" role="alert">{{ saveError() }}</p> }
          <div class="actions">
            <button class="primary" type="submit" [disabled]="saving() || !m.name.trim()">{{ saving() ? 'Saving…' : 'Save test case' }}</button>
            @if (!confirmingDelete()) {
              <button type="button" class="secondary danger" (click)="confirmingDelete.set(true)">Delete…</button>
            } @else {
              <span class="empty">Delete permanently? Past results stay in run history under this name.</span>
              <button type="button" class="secondary danger" (click)="remove(m)">Delete</button>
              <button type="button" class="secondary" (click)="confirmingDelete.set(false)">Keep</button>
            }
          </div>
        </form>
        <article class="panel">
          <div class="panel-title">
            <div><p class="eyebrow">EXECUTION HISTORY</p><h2>Results across runs</h2></div>
            <select [(ngModel)]="outcome" (ngModelChange)="filterHistory()" aria-label="Outcome filter">
              <option value="">All outcomes</option>@for (o of testOutcomes; track o) { <option [value]="o">{{ o }}</option> }
            </select>
          </div>
          @for (h of history.value()?.items ?? []; track h.resultId) {
            <div class="row">
              <a [routerLink]="['/runs', h.runId]">{{ h.runLabel }}</a>
              <span class="status" [attr.data-status]="h.outcome">{{ h.outcome }}</span>
              <span>{{ h.durationMs | number }} ms · {{ h.completedAtUtc | date: 'short' }}</span>
            </div>
          }
          <app-data-state [loading]="history.loading()" [loaded]="history.loaded()" [error]="history.error()" [empty]="!history.value()?.items?.length"
            emptyText="This test case has not been executed yet." (retry)="history.reload()" />
          <app-pager [page]="historyPage" [pageSize]="20" [totalCount]="history.value()?.totalCount ?? 0" (pageChange)="goToHistory($event)" />
        </article>
      </section>
    }`
})
export class TestCasePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly testCaseId = input.required<string>();
  protected readonly priorities = PRIORITIES;
  protected readonly outcomes = SIMULATED_OUTCOMES;
  protected readonly testOutcomes = TEST_OUTCOMES;
  protected readonly model = signal<TestCase | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly saveError = signal('');
  protected readonly confirmingDelete = signal(false);
  protected outcome: TestOutcome | '' = '';
  protected historyPage = 1;
  protected readonly history = loadable(() =>
    this.api.caseHistory(this.testCaseId(), { outcome: this.outcome, page: this.historyPage, pageSize: 20 }));

  ngOnInit(): void {
    this.load();
    this.history.reload();
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.testCase(this.testCaseId()).subscribe({
      next: testCase => { this.model.set(testCase); this.loading.set(false); },
      error: e => { this.loadError.set(problemMessage(e, 'Test case could not be loaded.')); this.loading.set(false); }
    });
  }

  protected filterHistory(): void { this.historyPage = 1; this.history.reload(); }
  protected goToHistory(page: number): void { this.historyPage = page; this.history.reload(); }

  protected save(testCase: TestCase): void {
    this.saving.set(true);
    this.saveError.set('');
    const { id, testSuiteId, ...body } = testCase;
    this.api.saveCase(testSuiteId, body, id).subscribe({
      next: saved => void this.router.navigate(['/suites', saved.testSuiteId, 'cases']),
      error: e => { this.saving.set(false); this.saveError.set(problemMessage(e, 'Test case could not be saved.')); }
    });
  }

  protected remove(testCase: TestCase): void {
    this.saveError.set('');
    this.api.deleteCase(testCase.id).subscribe({
      next: () => void this.router.navigate(['/suites', testCase.testSuiteId, 'cases']),
      error: e => { this.confirmingDelete.set(false); this.saveError.set(problemMessage(e, 'Test case could not be deleted.')); }
    });
  }
}
