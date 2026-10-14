import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DecisionEvaluation } from '../../core/models';
import { DecisionSupportPanel } from './decision-support-panel';

const evidence = [
  { optionId: 'sig-aaa', signature: 'aaa', category: 'ApiError' as const, normalizedMessage: 'Simulated ApiError failure.', sampleMessage: 'Simulated ApiError failure.', occurrencesInRun: 20, recurrence30Days: 50, affectedTestCases: 2, triageSeverity: 'CRITICAL' },
  { optionId: 'sig-bbb', signature: 'bbb', category: 'Timeout' as const, normalizedMessage: 'Simulated Timeout failure.', sampleMessage: 'Simulated Timeout failure.', occurrencesInRun: 10, recurrence30Days: 10, affectedTestCases: 1, triageSeverity: 'HIGH' }
];

const evaluation = (overrides: Partial<DecisionEvaluation>): DecisionEvaluation => ({
  id: 'e1', testRunId: 'r1', provider: 'SmartDog', status: 'Succeeded', requestedAtUtc: '2026-10-14T10:00:00Z', completedAtUtc: '2026-10-14T10:00:01Z',
  correlationId: 'corr-1', question: 'Which failure signature in this run should be investigated first?',
  criteria: [{ id: 'occurrencesInRun', name: 'Occurrences in this run', weight: 0.35, direction: 'maximize', source: 'Failed results in this run.' },
    { id: 'triageSeverity', name: 'Triage severity (LogTriageAssistant)', weight: 0.25, direction: 'maximize', source: 'Highest severity.' }],
  minimumOccurrences: null, evidence, evidenceNotes: ['Triage severity comes from LogTriageAssistant analysis x.'], failureAnalysisId: 'x',
  externalEvaluationId: 'sd-1', lastError: null,
  result: {
    evaluationId: 'sd-1', status: 'Ranked', method: 'Weighted sum.', limitations: 'Not a prediction.', notes: ['1 note'], infeasible: [],
    criteria: [], ranking: [
      { rank: 1, optionId: 'sig-aaa', name: 'ApiError: Simulated ApiError failure.', score: 1, explanation: 'Rank 1 of 2 with score 1.000. Strongest: …', tiedWithOptionIds: [],
        contributions: [{ criterionId: 'triageSeverity', rawValue: 4, normalizedValue: 1, normalizedWeight: 0.25, contribution: 0.25 }] },
      { rank: 2, optionId: 'sig-bbb', name: 'Timeout: Simulated Timeout failure.', score: 0, explanation: 'Rank 2 of 2 with score 0.000.', tiedWithOptionIds: [], contributions: [] }
    ]
  },
  ...overrides
});

describe('DecisionSupportPanel', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(history: DecisionEvaluation[], failedCount = 30) {
    const fixture = TestBed.createComponent(DecisionSupportPanel);
    fixture.componentRef.setInput('runId', 'r1');
    fixture.componentRef.setInput('runFinished', true);
    fixture.componentRef.setInput('failedCount', failedCount);
    fixture.detectChanges();
    backend.expectOne('/api/runs/r1/decision-support').flush(history);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('shows the ranking with explanations, evidence, notes and links to the failed tests', async () => {
    const fixture = await render([evaluation({})]);
    const element = fixture.nativeElement as HTMLElement;
    const text = element.textContent ?? '';

    expect(text).toContain('#1 ApiError: Simulated ApiError failure.');
    expect(text).toContain('Rank 1 of 2 with score 1.000');
    expect(text).toContain('CRITICAL');
    expect(text).toContain('Triage severity comes from LogTriageAssistant analysis x.');
    expect(text).toContain('Not a prediction.');
    const link = element.querySelector('a[href*="signature=aaa"]') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toContain('/runs/r1?signature=aaa#results');
    expect(link.textContent).toContain('20 failed tests');
  });

  it('submits the configured weights and minimum occurrences', async () => {
    const fixture = await render([]);
    const element = fixture.nativeElement as HTMLElement;
    const weight = element.querySelector('input[name="weight-occurrencesInRun"]') as HTMLInputElement;
    weight.value = '2';
    weight.dispatchEvent(new Event('input'));
    const minimum = element.querySelector('input[name="minimumOccurrences"]') as HTMLInputElement;
    minimum.value = '3';
    minimum.dispatchEvent(new Event('input'));

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const request = backend.expectOne(r => r.method === 'POST' && r.url === '/api/runs/r1/decision-support');
    expect(request.request.body).toEqual({
      weights: { occurrencesInRun: 2, recurrence30d: 0.25, affectedTestCases: 0.15, triageSeverity: 0.25 },
      minimumOccurrences: 3
    });
    request.flush(evaluation({ id: 'e2' }));
    fixture.detectChanges();
    expect(element.textContent).toContain('#1 ApiError');
  });

  it('makes unavailable evaluations explicit instead of showing a ranking', async () => {
    const fixture = await render([evaluation({ status: 'Unavailable', result: null, lastError: 'SmartDog is unavailable: connection refused' })]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Unavailable');
    expect(text).toContain('connection refused');
    expect(text).toContain('No ranking was produced');
    expect(text).not.toContain('#1 ');
  });

  it('rejects invalid weights on the client', async () => {
    const fixture = await render([]);
    const element = fixture.nativeElement as HTMLElement;
    const weight = element.querySelector('input[name="weight-recurrence30d"]') as HTMLInputElement;
    weight.value = '0';
    weight.dispatchEvent(new Event('input'));

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    backend.expectNone(r => r.method === 'POST');
    expect(element.textContent).toContain('must be greater than 0');
  });

  it('does not offer ranking for runs without failures', async () => {
    const fixture = await render([], 0);
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('form')).toBeNull();
    expect(element.textContent).toContain('no failed results to rank');
  });
});
