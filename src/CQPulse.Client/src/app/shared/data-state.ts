import { Component, input, output } from '@angular/core';

/**
 * Standard loading / error / empty presentation. The empty message only appears after a successful load that
 * returned nothing, so an API failure is never shown as "no data".
 */
@Component({
  selector: 'app-data-state',
  template: `
    @if (error()) {
      <div class="error state-error" role="alert">
        <span>{{ error() }}</span>
        <button type="button" (click)="retry.emit()">Retry</button>
      </div>
    } @else if (loading() && !loaded()) {
      <p class="empty" aria-live="polite">Loading…</p>
    } @else if (loaded() && empty()) {
      <p class="empty">{{ emptyText() }}</p>
    }
  `
})
export class DataState {
  readonly loading = input(false);
  readonly loaded = input(false);
  readonly error = input('');
  readonly empty = input(false);
  readonly emptyText = input('Nothing to show yet.');
  readonly retry = output<void>();
}
