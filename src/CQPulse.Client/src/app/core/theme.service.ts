import { Injectable, signal } from '@angular/core';

const THEME_KEY = 'cq-pulse-theme';

/** Single source of truth for the colour theme, shared by the top bar toggle and the Settings page. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly dark = signal(false);

  initialize(): void {
    let saved: string | null = null;
    try { saved = localStorage.getItem(THEME_KEY); } catch { /* storage unavailable */ }
    const prefersDark = typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
    this.set(saved === 'dark' || (!saved && prefersDark));
  }

  toggle(): void { this.set(!this.dark()); }

  set(dark: boolean): void {
    this.dark.set(dark);
    document.documentElement.dataset['theme'] = dark ? 'dark' : 'light';
    try { localStorage.setItem(THEME_KEY, dark ? 'dark' : 'light'); } catch { /* storage unavailable */ }
  }
}
