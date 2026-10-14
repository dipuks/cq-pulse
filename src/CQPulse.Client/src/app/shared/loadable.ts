import { DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, Subject, catchError, map, of, switchMap, tap } from 'rxjs';
import { problemMessage } from '../core/errors';

export interface Loadable<T> {
  readonly value: () => T | undefined;
  readonly loading: () => boolean;
  /** True once a request has completed successfully at least once; empty states should wait for this. */
  readonly loaded: () => boolean;
  readonly error: () => string;
  reload(): void;
}

/**
 * Tracks one API read for a component: loading, error message, and value. A new reload cancels any request
 * still in flight (so fast filter changes cannot resolve out of order). Must be created in an injection context.
 */
export function loadable<T>(source: () => Observable<T>): Loadable<T> {
  const destroyRef = inject(DestroyRef);
  const value = signal<T | undefined>(undefined);
  const loading = signal(false);
  const loaded = signal(false);
  const error = signal('');
  const trigger = new Subject<void>();

  trigger.pipe(
    tap(() => { loading.set(true); error.set(''); }),
    switchMap(() => source().pipe(
      map(result => ({ ok: true as const, result })),
      catchError((failure: unknown) => of({ ok: false as const, failure })))),
    takeUntilDestroyed(destroyRef)
  ).subscribe(outcome => {
    loading.set(false);
    if (outcome.ok) {
      value.set(outcome.result);
      loaded.set(true);
    } else {
      error.set(problemMessage(outcome.failure, 'The data could not be loaded.'));
    }
  });

  return {
    value: value.asReadonly(),
    loading: loading.asReadonly(),
    loaded: loaded.asReadonly(),
    error: error.asReadonly(),
    reload: () => trigger.next()
  };
}
