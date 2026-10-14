import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { InsightsPage } from './insights.page';

const window = { fromUtc: '2026-09-15T00:00:00Z', toUtc: '2026-10-15T00:00:00Z', previousFromUtc: '2026-08-16T00:00:00Z' };

describe('InsightsPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [InsightsPage], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  it('shows flagged days with their evidence, signatures, flaky caveats and the no-prediction statement', () => {
    const fixture = TestBed.createComponent(InsightsPage);
    fixture.detectChanges();
    backend.expectOne(r => r.url === '/api/projects').flush({ items: [], totalCount: 0, page: 1, pageSize: 100 });
    backend.expectOne(r => r.url === '/api/insights/anomalies').flush({
      window, baselineDays: 14, minimumBaselineDays: 5, minimumDailyResults: 20, threshold: 3, method: 'z-score rule.',
      days: [
        { date: '2026-10-13', results: 100, failed: 10, failureRate: 0.1, baselineMean: 0.1, baselineStdDev: 0.01, baselineDays: 14, zScore: 0, isAnomaly: false, assessment: 'Within expected range' },
        { date: '2026-10-14', results: 100, failed: 60, failureRate: 0.6, baselineMean: 0.1, baselineStdDev: 0.01, baselineDays: 14, zScore: 50, isAnomaly: true, assessment: 'Failure rate unusually high' }
      ]
    });
    backend.expectOne(r => r.url === '/api/insights/failure-signatures').flush({
      window, totalFailures: 70, signatures: [{
        signature: 'abc123def456', category: 'Timeout', normalizedMessage: 'Simulated Timeout failure.', sampleMessage: 'Simulated Timeout failure.',
        occurrences: 40, previousOccurrences: 10, affectedRuns: 3, affectedTestCases: 2, firstSeenUtc: '2026-10-01T00:00:00Z', lastSeenUtc: '2026-10-14T00:00:00Z',
        messageVariants: 1, countsAreUpperBounds: false
      }]
    });
    backend.expectOne(r => r.url === '/api/insights/flaky-tests').flush({
      window, minimumExecutions: 5, casesEvaluated: 4, candidates: [{
        testCaseId: 't1', testCaseName: 'Coin flip', testSuiteId: 's1', suiteName: 'Checkout', configuredOutcome: 'Random',
        executions: 10, passed: 5, failed: 5, skipped: 0, failureRate: 0.5, outcomeChanges: 6, randomnessIsConfigured: true
      }]
    });
    backend.expectOne(r => r.url === '/api/insights/quality-trends').flush({ window, projects: [], predictionStatus: 'Not offered: observed history only.' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('1 flagged of 2 assessable days');
    expect(text).toContain('Failure rate unusually high');
    expect(text).toContain('z = 50');
    expect(text).toContain('40×');
    expect(text).toContain('+30 vs previous');
    expect(text).toContain('configured with a Random simulated outcome');
    expect(text).toContain('Prediction: Not offered');
  });
});
