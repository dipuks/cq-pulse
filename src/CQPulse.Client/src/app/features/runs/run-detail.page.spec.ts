import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RunDetailPage } from './run-detail.page';

describe('RunDetailPage signature filter', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [RunDetailPage], providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  it('requests only the failures of the signature in the query parameter and says so', () => {
    const fixture = TestBed.createComponent(RunDetailPage);
    fixture.componentRef.setInput('runId', 'r1');
    fixture.componentRef.setInput('signature', 'abc123def456');
    fixture.detectChanges();
    TestBed.tick();

    const results = backend.expectOne(r => r.url === '/api/runs/r1/results');
    expect(results.request.params.get('signature')).toBe('abc123def456');
    results.flush({ items: [], totalCount: 0, page: 1, pageSize: 50 });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Showing failed tests with failure signature abc123def456');
  });

  it('requests all results without a signature', () => {
    const fixture = TestBed.createComponent(RunDetailPage);
    fixture.componentRef.setInput('runId', 'r1');
    fixture.detectChanges();
    TestBed.tick();

    const results = backend.expectOne(r => r.url === '/api/runs/r1/results');
    expect(results.request.params.has('signature')).toBe(false);
  });
});
