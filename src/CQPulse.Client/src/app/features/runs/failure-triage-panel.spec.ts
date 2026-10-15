import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FailureAnalysis } from '../../core/models';
import { FailureTriagePanel } from './failure-triage-panel';

const analysis = (overrides: Partial<FailureAnalysis>): FailureAnalysis => ({
  id: 'a1', testRunId: 'r1', provider: 'LogTriageAssistant', status: 'Succeeded', requestedAtUtc: '2026-10-13T10:00:00Z',
  completedAtUtc: '2026-10-13T10:00:02Z', attempts: 1, lastError: null, inputFailureCount: 30, inputTruncated: false,
  correlationId: 'corr-1', externalAnalysisId: 'ext-1', severity: 'HIGH', summary: 'Detected 30 actionable findings.', signalScore: 90,
  topCategory: 'Network', findings: [{ lineNumber: 1, endLineNumber: 1, message: 'm', severity: 'HIGH', category: 'Network', evidence: "Test 'Login' failed: Simulated Timeout failure.", suggestion: 'Check latency.' }],
  recommendations: ['Check latency.'], ...overrides
});

describe('FailureTriagePanel', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  function render(runFinished = true, failedCount = 30) {
    const fixture = TestBed.createComponent(FailureTriagePanel);
    fixture.componentRef.setInput('runId', 'r1');
    fixture.componentRef.setInput('runFinished', runFinished);
    fixture.componentRef.setInput('failedCount', failedCount);
    fixture.detectChanges();
    return fixture;
  }

  it('offers analysis for a finished run with failures and requests it', () => {
    const fixture = render();
    backend.expectOne('/api/runs/r1/failure-analyses').flush([]);
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector('button')!;
    expect(button.textContent).toContain('Analyze failures');

    button.click();
    backend.expectOne(r => r.method === 'POST' && r.url === '/api/runs/r1/failure-analyses').flush(analysis({ status: 'Pending' }));
    backend.expectOne('/api/runs/r1/failure-analyses').flush([analysis({ status: 'Pending', severity: null, findings: [] })]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Analysis in progress');
  });

  it('says when a pending analysis is waiting for a queue slot instead of running', () => {
    const fixture = render();
    backend.expectOne('/api/runs/r1/failure-analyses')
      .flush([analysis({ status: 'Pending', completedAtUtc: null, severity: null, findings: [], waitingForQueueSlot: true })]);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Waiting for a free analysis slot');
    expect(text).not.toContain('Analysis in progress');
  });

  it('shows provider output with an explicit heuristic label', () => {
    const fixture = render();
    backend.expectOne('/api/runs/r1/failure-analyses').flush([analysis({})]);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('HIGH');
    expect(text).toContain('not a probability');
    expect(text).toContain("Simulated Timeout failure");
    expect(text).toContain('Correlation ID corr-1');
  });

  it('shows the recorded error and a retry for unavailable analyses', () => {
    const fixture = render();
    backend.expectOne('/api/runs/r1/failure-analyses').flush([analysis({ status: 'Unavailable', severity: null, findings: [], lastError: 'LogTriageAssistant is unavailable: connection refused' })]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('connection refused');

    (element.querySelector('button') as HTMLButtonElement).click();
    expect(backend.expectOne(r => r.method === 'POST' && r.url === '/api/failure-analyses/a1/retry')).toBeTruthy();
  });

  it('does not offer analysis for runs without failures', () => {
    const fixture = render(true, 0);
    backend.expectOne('/api/runs/r1/failure-analyses').flush([]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('button')).toBeNull();
    expect(element.textContent).toContain('no failed results');
  });
});
