import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RunDetail, SimulatorLimits } from '../../core/models';
import { SimulatorPage, defaultRequest, mergeRunSnapshot, validateRequest } from './simulator.page';

const limits: SimulatorLimits = { maxTestCount: 50_000, maxConcurrency: 128, maxDurationMs: 60_000, largeWorkloadThreshold: 10_000 };

describe('mergeRunSnapshot', () => {
  const run = (overrides: Partial<RunDetail>): RunDetail => ({
    id: 'r1', projectId: 'p1', runLabel: 'Run', status: 'Running', requestedAtUtc: '2026-10-14T10:00:00Z', totalTests: 20_000,
    completedTests: 0, passedCount: 0, failedCount: 0, skippedCount: 0, configurationSnapshot: '{}', rerunIds: [], ...overrides
  });

  it('keeps newer live progress when the persisted snapshot lags behind', () => {
    const live = run({ completedTests: 7_800, passedCount: 5_850, failedCount: 1_560, skippedCount: 390 });
    const merged = mergeRunSnapshot(live, run({ completedTests: 7_500, passedCount: 5_625, failedCount: 1_500, skippedCount: 375, startedAtUtc: '2026-10-14T10:00:01Z' }));

    expect([merged.completedTests, merged.passedCount, merged.failedCount, merged.skippedCount]).toEqual([7_800, 5_850, 1_560, 390]);
    expect(merged.startedAtUtc).toBe('2026-10-14T10:00:01Z');
  });

  it('takes the snapshot when it is ahead, terminal or for another run', () => {
    const live = run({ completedTests: 7_800 });
    expect(mergeRunSnapshot(live, run({ completedTests: 8_000 })).completedTests).toBe(8_000);
    expect(mergeRunSnapshot(live, run({ status: 'Cancelled', completedTests: 7_500 })).completedTests).toBe(7_500);
    expect(mergeRunSnapshot(live, run({ id: 'r2', completedTests: 10 })).completedTests).toBe(10);
    expect(mergeRunSnapshot(null, run({ completedTests: 10 })).completedTests).toBe(10);
  });
});

describe('validateRequest', () => {
  const valid = { ...defaultRequest(), projectId: 'p1' };

  it('accepts the default request for a chosen project', () => {
    expect(validateRequest(valid, limits)).toEqual([]);
  });

  it('enforces the server-reported limits', () => {
    expect(validateRequest({ ...valid, testCount: 50_001 }, limits).join()).toContain('between 1 and 50,000');
    expect(validateRequest({ ...valid, maxConcurrency: 129 }, limits).join()).toContain('between 1 and 128');
    expect(validateRequest({ ...valid, maximumDurationMs: 60_001 }, limits).join()).toContain('must not exceed');
  });

  it('requires percentages to total 100 and categories when failures are configured', () => {
    expect(validateRequest({ ...valid, passPercentage: 50 }, limits).join()).toContain('total 100');
    expect(validateRequest({ ...valid, failureCategories: [] }, limits).join()).toContain('failure category');
    expect(validateRequest({ ...valid, passPercentage: 96, failurePercentage: 0, failureCategories: [] }, limits)).toEqual([]);
  });

  it('rejects inverted durations and missing project', () => {
    expect(validateRequest({ ...valid, minimumDurationMs: 100, maximumDurationMs: 50 }, limits).join()).toContain('at least the minimum');
    expect(validateRequest({ ...valid, projectId: '' }, limits).join()).toContain('Choose a project');
  });
});

describe('SimulatorPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [SimulatorPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    backend = TestBed.inject(HttpTestingController);
  });

  async function render() {
    const fixture = TestBed.createComponent(SimulatorPage);
    fixture.detectChanges();
    backend.expectOne('/api/simulator/limits').flush(limits);
    backend.expectOne(r => r.url === '/api/projects').flush({ items: [{ id: 'p1', name: 'Web Portal', status: 'Active', createdAtUtc: '' }], totalCount: 1, page: 1, pageSize: 100 });
    backend.expectOne(r => r.url === '/api/projects/p1/suites').flush({ items: [], totalCount: 0, page: 1, pageSize: 100 });
    fixture.detectChanges();
    await fixture.whenStable(); // ngModel registers its controls asynchronously
    return fixture;
  }

  it('only offers to queue after a successful preview, and editing clears the preview', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).not.toContain('Queue simulation');

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    const preview = backend.expectOne('/api/simulator/preview');
    expect(preview.request.body.projectId).toBe('p1');
    preview.flush({
      testCount: 100, expectedPassed: 88, expectedFailed: 8, expectedSkipped: 4, maxConcurrency: 12, resourceWarning: 'ok',
      projectName: 'Web Portal', testSuiteName: null, minimumDurationMs: 20, maximumDurationMs: 90, failureCategories: [], randomSeed: null,
      runLabel: 'Simulator run', estimatedDurationSeconds: 0.5
    });
    fixture.detectChanges();
    expect(element.textContent).toContain('Queue simulation');
    expect(element.textContent).toContain('Passed (exact)');

    const count = element.querySelector('input[name="count"]') as HTMLInputElement;
    count.value = '250';
    count.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    expect(element.textContent).not.toContain('Queue simulation');
  });

  it('shows client validation errors without calling the API', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;
    const pass = element.querySelector('input[name="pass"]') as HTMLInputElement;
    pass.value = '10';
    pass.dispatchEvent(new Event('input', { bubbles: true }));

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();

    backend.expectNone('/api/simulator/preview');
    expect(element.textContent).toContain('must total 100');
  });

  it('shows the server message when the preview is rejected', async () => {
    const fixture = await render();
    const element = fixture.nativeElement as HTMLElement;

    (element.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    backend.expectOne('/api/simulator/preview').flush({ title: 'Resource not found.', detail: 'The selected project does not exist.' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element.textContent).toContain('The selected project does not exist.');
  });
});
