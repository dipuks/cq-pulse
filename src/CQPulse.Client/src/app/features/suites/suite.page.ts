import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { PRIORITIES, SIMULATED_OUTCOMES, SaveSuite, SaveTestCase, TestCase, TestPriority } from '../../core/models';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';
import { Pager } from '../../shared/pager';

const newCase = (): SaveTestCase => ({ name: '', description: '', enabled: true, priority: 'Medium', simulatedOutcome: 'Random', simulatedDurationMs: 100 });

@Component({
  imports: [FormsModule, RouterLink, RouterLinkActive, DatePipe, DecimalPipe, DataState, Pager],
  templateUrl: './suite.page.html'
})
export class SuitePage implements OnInit {
  private readonly api = inject(ApiService);
  readonly suiteId = input.required<string>();
  /** From route data: 'overview' for /suites/:id, 'cases' for /suites/:id/cases. */
  readonly view = input<'overview' | 'cases'>('overview');

  protected readonly priorities = PRIORITIES;
  protected readonly outcomes = SIMULATED_OUTCOMES;
  protected readonly pageSize = 25;
  protected readonly suite = loadable(() => this.api.suite(this.suiteId()));
  protected readonly runs = loadable(() => this.api.runs({ testSuiteId: this.suiteId(), page: this.runPage, pageSize: 10 }));
  protected readonly cases = loadable(() => this.api.cases(this.suiteId(), {
    search: this.search.trim(), enabled: this.enabled, priority: this.priority, sort: this.sort, page: this.casePage, pageSize: this.pageSize
  }));

  protected details: SaveSuite & { name: string } = { name: '', description: '' };
  protected draft = newCase();
  protected search = '';
  protected enabled: '' | 'true' | 'false' = '';
  protected priority: TestPriority | '' = '';
  protected sort = 'name';
  protected casePage = 1;
  protected runPage = 1;
  protected readonly suiteMessage = signal('');
  protected readonly suiteSaved = signal(false);
  protected readonly caseError = signal('');
  protected readonly rowError = signal('');
  protected readonly busyCaseId = signal<string | null>(null);

  constructor() {
    effect(() => {
      const suite = this.suite.value();
      if (suite) this.details = { name: suite.name, description: suite.description ?? '' };
    });
  }

  ngOnInit(): void {
    this.suite.reload();
    if (this.view() === 'cases') this.cases.reload(); else this.runs.reload();
  }

  protected applyFilters(): void { this.casePage = 1; this.cases.reload(); }
  protected goToCases(page: number): void { this.casePage = page; this.cases.reload(); }
  protected goToRuns(page: number): void { this.runPage = page; this.runs.reload(); }

  protected saveSuite(): void {
    const suite = this.suite.value();
    if (!suite) return;
    this.suiteMessage.set('');
    this.api.saveSuite(suite.projectId, this.details, suite.id).subscribe({
      next: () => { this.suiteSaved.set(true); this.suiteMessage.set('Suite saved.'); this.suite.reload(); },
      error: e => { this.suiteSaved.set(false); this.suiteMessage.set(problemMessage(e, 'Suite could not be saved.')); }
    });
  }

  protected addCase(): void {
    this.caseError.set('');
    this.api.saveCase(this.suiteId(), this.draft).subscribe({
      next: () => { this.draft = newCase(); this.cases.reload(); this.suite.reload(); },
      error: e => this.caseError.set(problemMessage(e, 'Test case could not be created.'))
    });
  }

  protected toggle(testCase: TestCase): void {
    this.busyCaseId.set(testCase.id);
    this.rowError.set('');
    this.api.setCaseEnabled(testCase.id, !testCase.enabled).subscribe({
      next: () => { this.busyCaseId.set(null); this.cases.reload(); this.suite.reload(); },
      error: e => { this.busyCaseId.set(null); this.rowError.set(problemMessage(e, 'The test case could not be updated.')); }
    });
  }
}
