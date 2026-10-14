import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { PRIORITIES, SIMULATED_OUTCOMES, TestCase } from '../../core/models';
import { DataState } from '../../shared/data-state';

@Component({
  imports: [FormsModule, RouterLink, DataState],
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">TEST CASE</p><h1>{{ model()?.name || 'Test case' }}</h1></div>
      @if (model(); as m) { <a [routerLink]="['/suites', m.testSuiteId, 'cases']">← Suite</a> }
    </section>
    <app-data-state [loading]="loading()" [loaded]="!loading()" [error]="loadError()" (retry)="load()" />
    @if (model(); as m) {
      <form class="panel form" (ngSubmit)="save(m)">
        <label>Name<input [(ngModel)]="m.name" name="name" required maxlength="200"></label>
        <label>Description<textarea [(ngModel)]="m.description" name="description" maxlength="4000"></textarea></label>
        <label>Priority
          <select [(ngModel)]="m.priority" name="priority">@for (p of priorities; track p) { <option [value]="p">{{ p }}</option> }</select>
        </label>
        <label>Simulated outcome
          <select [(ngModel)]="m.simulatedOutcome" name="outcome">@for (o of outcomes; track o) { <option [value]="o">{{ o }}</option> }</select>
        </label>
        <label>Duration (ms)<input type="number" [(ngModel)]="m.simulatedDurationMs" name="duration" min="0" max="60000"></label>
        <label class="check"><input type="checkbox" [(ngModel)]="m.enabled" name="enabled"> Enabled</label>
        @if (saveError()) { <p class="error" role="alert">{{ saveError() }}</p> }
        <button class="primary" type="submit" [disabled]="saving() || !m.name.trim()">{{ saving() ? 'Saving…' : 'Save test case' }}</button>
      </form>
    }`
})
export class TestCasePage implements OnInit {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  readonly testCaseId = input.required<string>();
  protected readonly priorities = PRIORITIES;
  protected readonly outcomes = SIMULATED_OUTCOMES;
  protected readonly model = signal<TestCase | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly saveError = signal('');

  ngOnInit(): void { this.load(); }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.api.testCase(this.testCaseId()).subscribe({
      next: testCase => { this.model.set(testCase); this.loading.set(false); },
      error: e => { this.loadError.set(problemMessage(e, 'Test case could not be loaded.')); this.loading.set(false); }
    });
  }

  protected save(testCase: TestCase): void {
    this.saving.set(true);
    this.saveError.set('');
    const { id, testSuiteId, ...body } = testCase;
    this.api.saveCase(testSuiteId, body, id).subscribe({
      next: saved => void this.router.navigate(['/suites', saved.testSuiteId, 'cases']),
      error: e => { this.saving.set(false); this.saveError.set(problemMessage(e, 'Test case could not be saved.')); }
    });
  }
}
