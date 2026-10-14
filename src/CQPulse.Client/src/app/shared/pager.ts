import { Component, computed, input, output } from '@angular/core';

/** Previous/next pager for server-side paginated lists. */
@Component({
  selector: 'app-pager',
  template: `
    @if (totalCount() > pageSize()) {
      <nav class="pager" aria-label="Pagination">
        <span>Page {{ page() }} of {{ pageCount() }} · {{ totalCount() }} total</span>
        <button type="button" class="secondary" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">← Previous</button>
        <button type="button" class="secondary" [disabled]="page() >= pageCount()" (click)="pageChange.emit(page() + 1)">Next →</button>
      </nav>
    }`
})
export class Pager {
  readonly page = input(1);
  readonly pageSize = input(25);
  readonly totalCount = input(0);
  readonly pageChange = output<number>();
  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
}
