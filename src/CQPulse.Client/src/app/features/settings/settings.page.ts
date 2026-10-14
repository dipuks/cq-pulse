import { DatePipe } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { ApiService } from '../../core/api.service';
import { ThemeService } from '../../core/theme.service';
import { DataState } from '../../shared/data-state';
import { loadable } from '../../shared/loadable';

@Component({
  imports: [DatePipe, DataState],
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">PREFERENCES</p><h1>Settings</h1><p class="intro">Display preferences and connected services.</p></div>
    </section>
    <section class="split">
      <section class="panel form">
        <h2>Appearance</h2>
        <label>Color theme
          <select [value]="theme.dark() ? 'dark' : 'light'" (change)="setTheme($event)" name="theme">
            <option value="light">Light</option>
            <option value="dark">Dark</option>
          </select>
        </label>
        <p class="empty">Your theme is retained in this browser. Authentication uses a JWT stored in this browser; signing out removes it.</p>
      </section>
      <section class="panel">
        <div class="panel-title">
          <div><p class="eyebrow">INTEGRATIONS</p><h2>Connected services</h2></div>
          <button type="button" class="secondary" (click)="status.reload()">Check again</button>
        </div>
        @for (s of status.value() ?? []; track s.name) {
          <div class="row">
            <strong>{{ s.name }}</strong>
            <span class="status" [attr.data-status]="badge(s.status)">{{ s.status }}</span>
          </div>
          <p class="empty"><small>{{ s.detail }}{{ s.baseUrl ? ' · ' + s.baseUrl : '' }} · checked {{ s.checkedAtUtc | date: 'mediumTime' }}</small></p>
        }
        <app-data-state [loading]="status.loading()" [loaded]="status.loaded()" [error]="status.error()" (retry)="status.reload()" />
        <p class="empty"><small>Optional services: CQ Pulse keeps running and persisting results when they are unavailable.</small></p>
      </section>
    </section>`
})
export class SettingsPage implements OnInit {
  private readonly api = inject(ApiService);
  protected readonly theme = inject(ThemeService);
  protected readonly status = loadable(() => this.api.integrationStatus());

  ngOnInit(): void { this.status.reload(); }

  protected setTheme(event: Event): void { this.theme.set((event.target as HTMLSelectElement).value === 'dark'); }

  protected badge(status: string): string {
    return status === 'Healthy' ? 'Passed' : status === 'Unavailable' ? 'Failed' : 'Skipped';
  }
}
