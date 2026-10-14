import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { problemMessage } from '../../core/errors';
import { DecisionEvaluation, DecisionOptionEvidence } from '../../core/models';

interface WeightField { id: string; label: string; value: number; }

const DEFAULT_WEIGHTS: WeightField[] = [
  { id: 'occurrencesInRun', label: 'Occurrences in this run', value: 0.35 },
  { id: 'recurrence30d', label: 'Project recurrence (30 days)', value: 0.25 },
  { id: 'affectedTestCases', label: 'Affected catalog test cases', value: 0.15 },
  { id: 'triageSeverity', label: 'Triage severity (LogTriageAssistant)', value: 0.25 }
];

/**
 * Decision support for a finished run: SmartDog ranks the run's failure signatures from persisted evidence. Shows the
 * selected evaluation exactly as recorded (submitted evidence, ranking and explanations, or the failure reason).
 */
@Component({
  selector: 'app-decision-support',
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe],
  templateUrl: './decision-support-panel.html'
})
export class DecisionSupportPanel {
  private readonly api = inject(ApiService);
  readonly runId = input.required<string>();
  readonly runFinished = input(false);
  readonly failedCount = input(0);

  protected readonly weights = signal<WeightField[]>(DEFAULT_WEIGHTS.map(w => ({ ...w })));
  protected minimumOccurrences = 1;
  protected readonly history = signal<DecisionEvaluation[]>([]);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => this.history().find(h => h.id === this.selectedId()) ?? this.history()[0] ?? null);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly canEvaluate = computed(() => this.runFinished() && this.failedCount() > 0);

  constructor() {
    effect(() => {
      const runId = this.runId();
      this.runFinished();
      untracked(() => this.load(runId));
    });
  }

  protected evaluate(): void {
    const invalid = this.weights().find(w => !(w.value > 0 && w.value <= 1000));
    if (invalid) { this.error.set(`Weight for "${invalid.label}" must be greater than 0 and at most 1000.`); return; }
    if (!(Number.isInteger(this.minimumOccurrences) && this.minimumOccurrences >= 1)) { this.error.set('Minimum occurrences must be a whole number of at least 1.'); return; }
    this.busy.set(true);
    this.error.set('');
    const weights = Object.fromEntries(this.weights().map(w => [w.id, w.value]));
    this.api.evaluateDecision(this.runId(), { weights, minimumOccurrences: this.minimumOccurrences }).subscribe({
      next: evaluation => {
        this.busy.set(false);
        this.history.set([evaluation, ...this.history().filter(h => h.id !== evaluation.id)]);
        this.selectedId.set(evaluation.id);
      },
      error: e => { this.busy.set(false); this.error.set(problemMessage(e, 'The ranking could not be requested.')); }
    });
  }

  protected resetWeights(): void { this.weights.set(DEFAULT_WEIGHTS.map(w => ({ ...w }))); this.minimumOccurrences = 1; }

  protected evidenceFor(evaluation: DecisionEvaluation, optionId: string): DecisionOptionEvidence | undefined {
    return evaluation.evidence.find(e => e.optionId === optionId);
  }

  protected criterionName(evaluation: DecisionEvaluation, id: string): string {
    return evaluation.criteria.find(c => c.id === id)?.name ?? id;
  }

  protected statusBadge(status: string): string {
    return status === 'Succeeded' ? 'Passed' : status === 'Unavailable' ? 'Skipped' : status === 'Failed' ? 'Failed' : 'Running';
  }

  private load(runId: string): void {
    this.api.decisionHistory(runId).subscribe({
      next: list => { this.history.set(list); this.loaded.set(true); },
      error: e => { this.loaded.set(true); this.error.set(problemMessage(e, 'Decision history could not be loaded.')); }
    });
  }
}
