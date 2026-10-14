import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { TrendPoint } from '../core/models';

interface Bar { x: number; width: number; label: string; segments: { y: number; height: number; kind: 'pass' | 'fail' | 'skip' }[]; }

/**
 * Stacked daily outcome bars (passed / failed / skipped) drawn as SVG from API trend points. The same numbers are
 * exposed in a visually hidden table for assistive technology; colours come from theme tokens.
 */
@Component({
  selector: 'app-trend-chart',
  imports: [DatePipe, DecimalPipe],
  template: `
    @if (points().length) {
      <figure class="trend-chart">
        <svg [attr.viewBox]="'0 0 ' + width + ' ' + height" preserveAspectRatio="none" role="img" [attr.aria-label]="summary()">
          @for (bar of bars(); track bar.label) {
            <g>
              <title>{{ bar.label }}</title>
              @for (segment of bar.segments; track $index) {
                <rect [attr.x]="bar.x" [attr.y]="segment.y" [attr.width]="bar.width" [attr.height]="segment.height" [attr.class]="segment.kind" />
              }
            </g>
          }
        </svg>
        <figcaption class="legend">
          <span><i class="swatch pass"></i>Passed</span><span><i class="swatch fail"></i>Failed</span><span><i class="swatch skip"></i>Skipped</span>
          <span class="axis">{{ points()[0].date | date: 'mediumDate' }} – {{ points()[points().length - 1].date | date: 'mediumDate' }} (UTC days)</span>
        </figcaption>
        <table class="visually-hidden">
          <caption>{{ caption() }}</caption>
          <thead><tr><th scope="col">Date (UTC)</th><th scope="col">Passed</th><th scope="col">Failed</th><th scope="col">Skipped</th></tr></thead>
          <tbody>
            @for (p of points(); track p.date) {
              <tr><th scope="row">{{ p.date }}</th><td>{{ p.passed | number }}</td><td>{{ p.failed | number }}</td><td>{{ p.skipped | number }}</td></tr>
            }
          </tbody>
        </table>
      </figure>
    }`
})
export class TrendChart {
  readonly points = input<TrendPoint[]>([]);
  readonly caption = input('Daily test outcomes');
  protected readonly width = 600;
  protected readonly height = 160;

  protected readonly bars = computed<Bar[]>(() => {
    const points = this.points();
    const max = Math.max(1, ...points.map(p => p.passed + p.failed + p.skipped));
    const slot = this.width / Math.max(points.length, 1);
    const barWidth = Math.max(2, slot * 0.7);
    return points.map((p, i) => {
      let top = this.height;
      const segments = ([['pass', p.passed], ['fail', p.failed], ['skip', p.skipped]] as const).map(([kind, value]) => {
        const height = (value / max) * (this.height - 4);
        top -= height;
        return { y: top, height, kind };
      }).filter(s => s.height > 0);
      return { x: i * slot + (slot - barWidth) / 2, width: barWidth, label: `${p.date}: ${p.passed} passed, ${p.failed} failed, ${p.skipped} skipped`, segments };
    });
  });

  protected readonly summary = computed(() => {
    const points = this.points();
    const total = points.reduce((sum, p) => sum + p.passed + p.failed + p.skipped, 0);
    const failed = points.reduce((sum, p) => sum + p.failed, 0);
    return `${this.caption()}: ${points.length} days, ${total} results, ${failed} failed.`;
  });
}
