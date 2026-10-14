import { Component, OnInit, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { PRIORITIES, SaveSuite, TestPriority } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [FormsModule, RouterLink, DataState],
  template: `
    <section class="page-heading">
      <div>
        <p class="eyebrow">SUITE</p>
        <h1>{{ suite.value()?.name || 'Test suite' }}</h1>
        <p class="intro">{{ suite.value()?.description }}</p>
      </div>
      @if (suite.value(); as s) { <a [routerLink]="['/projects', s.projectId, 'suites']">← Suites</a> }
    </section>
    <app-data-state [loading]="suite.loading()" [loaded]="suite.loaded()" [error]="suite.error()" (retry)="suite.reload()" />
    @if (suite.value()) {
      <section class="split">
        <form class="panel form" (ngSubmit)="saveSuite()">
          <h2>Suite details</h2>
          <label>Name<input [(ngModel)]="details.name" name="suiteName" required maxlength="200"></label>
          <label>Description<textarea [(ngModel)]="details.description" name="suiteDescription" maxlength="2000"></textarea></label>
          @if (suiteMessage()) { <p [class]="suiteSaved() ? 'empty' : 'error'" role="status">{{ suiteMessage() }}</p> }
          <button class="primary" type="submit" [disabled]="!details.name.trim()">Save suite</button>
        </form>
        <form class="panel form" (ngSubmit)="addCase()">
          <h2>New test case</h2>
          <label>Name<input [(ngModel)]="caseName" name="caseName" required maxlength="200"></label>
          <label>Priority
            <select [(ngModel)]="priority" name="priority">@for (p of priorities; track p) { <option [value]="p">{{ p }}</option> }</select>
          </label>
          @if (caseError()) { <p class="error" role="alert">{{ caseError() }}</p> }
          <button class="primary" type="submit" [disabled]="!caseName.trim()">Create case</button>
        </form>
      </section>
    }
    <article class="panel">
      <div class="table-row heading"><span>Test case</span><span>Priority</span><span>Outcome</span><span></span></div>
      @for (testCase of cases.value()?.items ?? []; track testCase.id) {
        <div class="table-row">
          <strong>{{ testCase.name }}<small>{{ testCase.enabled ? testCase.description : 'Disabled' }}</small></strong>
          <span>{{ testCase.priority }}</span>
          <span>{{ testCase.simulatedOutcome }}</span>
          <a [routerLink]="['/test-cases', testCase.id]">Edit →</a>
        </div>
      }
      <app-data-state [loading]="cases.loading()" [loaded]="cases.loaded()" [error]="cases.error()"
        [empty]="!cases.value()?.items?.length" emptyText="No test cases yet." (retry)="cases.reload()" />
    </article>`
})
export class SuitePage implements OnInit {
  private readonly api = inject(ApiService);
  readonly suiteId = input.required<string>();
  protected readonly priorities = PRIORITIES;
  protected readonly suite = loadable(() => this.api.suite(this.suiteId()));
  protected readonly cases = loadable(() => this.api.cases(this.suiteId(), { pageSize: 100 }));
  protected details: SaveSuite & { name: string } = { name: '', description: '' };
  protected caseName = '';
  protected priority: TestPriority = 'Medium';
  protected readonly suiteMessage = signal('');
  protected readonly suiteSaved = signal(false);
  protected readonly caseError = signal('');

  constructor() {
    // Refresh the editable copy whenever the suite (re)loads.
    effect(() => {
      const suite = this.suite.value();
      if (suite) this.details = { name: suite.name, description: suite.description ?? '' };
    });
  }

  ngOnInit(): void {
    this.loadSuite();
    this.cases.reload();
  }

  protected saveSuite(): void {
    const suite = this.suite.value();
    if (!suite) return;
    this.suiteMessage.set('');
    this.api.saveSuite(suite.projectId, this.details, suite.id).subscribe({
      next: () => { this.suiteSaved.set(true); this.suiteMessage.set('Suite saved.'); this.loadSuite(); },
      error: e => { this.suiteSaved.set(false); this.suiteMessage.set(problemMessage(e, 'Suite could not be saved.')); }
    });
  }

  protected addCase(): void {
    this.caseError.set('');
    this.api.saveCase(this.suiteId(), {
      name: this.caseName, description: '', enabled: true, priority: this.priority, simulatedOutcome: 'Random', simulatedDurationMs: 100
    }).subscribe({
      next: () => { this.caseName = ''; this.cases.reload(); this.loadSuite(); },
      error: e => this.caseError.set(problemMessage(e, 'Test case could not be created.'))
    });
  }

  private loadSuite(): void { this.suite.reload(); }
}
