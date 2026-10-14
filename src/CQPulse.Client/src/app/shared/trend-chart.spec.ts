import { TestBed } from '@angular/core/testing';
import { TrendChart } from './trend-chart';

describe('TrendChart', () => {
  it('draws stacked bars proportional to totals and exposes the numbers in an accessible table', () => {
    const fixture = TestBed.createComponent(TrendChart);
    fixture.componentRef.setInput('points', [
      { date: '2026-10-11', passed: 6, failed: 3, skipped: 1, averageDurationMs: 4 },
      { date: '2026-10-12', passed: 5, failed: 0, skipped: 0, averageDurationMs: 4 }
    ]);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const svg = element.querySelector('svg')!;
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toContain('2 days, 15 results, 3 failed');
    const groups = element.querySelectorAll('svg g');
    expect(groups.length).toBe(2);
    expect(groups[0].querySelectorAll('rect').length).toBe(3);
    expect(groups[1].querySelectorAll('rect').length).toBe(1); // zero-height segments are omitted
    const heights = [...groups[0].querySelectorAll('rect')].map(r => Number(r.getAttribute('height')));
    expect(heights[0] / heights[1]).toBeCloseTo(2, 5); // 6 passed vs 3 failed
    expect(element.querySelectorAll('table tbody tr').length).toBe(2);
  });

  it('renders nothing without data', () => {
    const fixture = TestBed.createComponent(TrendChart);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('svg')).toBeNull();
  });
});
