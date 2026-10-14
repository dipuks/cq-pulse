import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { Component, OnInit, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [FormsModule, RouterLink, DatePipe, DecimalPipe, PercentPipe, DataState],
  templateUrl: './insights.page.html'
})
export class InsightsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected days = 30;
  protected projectId = '';
  private readonly query = () => ({ days: this.days, projectId: this.projectId });

  protected readonly projects = loadable(() => this.api.projects({ pageSize: 100 }));
  protected readonly anomalies = loadable(() => this.api.anomalies(this.query()));
  protected readonly signatures = loadable(() => this.api.failureSignatures({ ...this.query(), top: 15 }));
  protected readonly flaky = loadable(() => this.api.flakyTests({ ...this.query(), minExecutions: 5 }));
  protected readonly trends = loadable(() => this.api.qualityTrends({ days: Math.min(this.days, 30) }));

  protected readonly flaggedDays = computed(() => (this.anomalies.value()?.days ?? []).filter(d => d.isAnomaly));
  protected readonly assessedDays = computed(() => (this.anomalies.value()?.days ?? []).filter(d => d.zScore != null).length);
  protected readonly daysWithData = computed(() => (this.anomalies.value()?.days ?? []).filter(d => d.results > 0));

  ngOnInit(): void {
    this.projects.reload();
    this.apply();
  }

  protected apply(): void {
    for (const resource of [this.anomalies, this.signatures, this.flaky, this.trends]) resource.reload();
  }

  protected change(current: number, previous: number): string {
    if (previous === 0) return current > 0 ? 'new' : '—';
    const delta = current - previous;
    return `${delta > 0 ? '+' : ''}${delta.toLocaleString()} vs previous`;
  }
}
