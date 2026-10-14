import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AnalyticsPage } from './analytics.page';

describe('AnalyticsPage', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AnalyticsPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  function render() {
    const fixture = TestBed.createComponent(AnalyticsPage);
    fixture.detectChanges();
    backend.expectOne(r => r.url === '/api/projects').flush({ items: [], totalCount: 0, page: 1, pageSize: 100 });
    return fixture;
  }

  // Regression: an API failure used to render as "No persisted outcomes yet".
  it('shows an error with retry, not an empty state, when the API fails', () => {
    const fixture = render();
    for (const url of ['/api/analytics/summary', '/api/analytics/execution-trends', '/api/analytics/failure-breakdown'])
      backend.expectOne(url).flush(null, { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Cannot reach the CQ Pulse API');
    expect(text).not.toContain('No persisted results match');
    expect(text).not.toContain('No results in this range');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.state-error button').length).toBe(3);
  });

  it('shows loading before data arrives and empty states only for a successful empty response', () => {
    const fixture = render();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Loading…');

    backend.expectOne('/api/analytics/summary').flush({ total: 0, passed: 0, failed: 0, skipped: 0, passRate: 0, failureRate: 0, averageDurationMs: null });
    backend.expectOne('/api/analytics/execution-trends').flush([]);
    backend.expectOne('/api/analytics/failure-breakdown').flush([]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No persisted results match these filters.');
    expect(text).not.toContain('Loading…');
  });

  it('renders persisted totals, trends and categories', () => {
    const fixture = render();
    backend.expectOne('/api/analytics/summary').flush({ total: 24125, passed: 19281, failed: 3699, skipped: 1145, passRate: 79.9, failureRate: 15.3, averageDurationMs: 4.3 });
    backend.expectOne('/api/analytics/execution-trends').flush([{ date: '2026-10-11', passed: 10, failed: 2, skipped: 1, averageDurationMs: 5 }]);
    backend.expectOne('/api/analytics/failure-breakdown').flush([{ category: 'ApiError', count: 1234 }, { category: 'Timeout', count: 617 }]);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('24,125');
    expect(text).toContain('79.9%');
    expect(text).toContain('ApiError');
    const bars = (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.failure-row i');
    expect(bars[0].style.width).toBe('100%');
    expect(bars[1].style.width).toBe('50%');
  });
});
