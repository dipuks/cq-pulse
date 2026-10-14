import { Component, inject } from '@angular/core';
import { ThemeService } from '../../core/theme.service';

@Component({
  template: `
    <section class="page-heading">
      <div><p class="eyebrow">PREFERENCES</p><h1>Settings</h1><p class="intro">Your display preference is retained in this browser.</p></div>
    </section>
    <section class="panel form">
      <h2>Appearance</h2>
      <label>Color theme
        <select [value]="theme.dark() ? 'dark' : 'light'" (change)="setTheme($event)" name="theme">
          <option value="light">Light</option>
          <option value="dark">Dark</option>
        </select>
      </label>
      <p class="empty">Authentication uses a JWT stored in this browser. Signing out removes it.</p>
    </section>`
})
export class SettingsPage {
  protected readonly theme = inject(ThemeService);
  protected setTheme(event: Event): void { this.theme.set((event.target as HTMLSelectElement).value === 'dark'); }
}
