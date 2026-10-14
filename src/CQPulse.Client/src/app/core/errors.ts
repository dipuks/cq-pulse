import { HttpErrorResponse } from '@angular/common/http';

/**
 * Turns an HTTP failure into a message for the user. Uses the API's Problem Details (detail, then the first
 * validation error, then title) and distinguishes an unreachable API from server-side errors.
 */
export function problemMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.status === 0) return 'Cannot reach the CQ Pulse API. Make sure it is running and try again.';
  if (error.status === 401) return 'Your session has expired. Please sign in again.';
  if (error.status === 403) return 'You do not have permission to perform this action.';

  const body = error.error as { detail?: string; title?: string; errors?: Record<string, string[]> } | null;
  const firstValidationError = body?.errors ? Object.values(body.errors).flat()[0] : undefined;
  if (error.status === 400 && body?.errors) return validationSummary(body.errors);
  return body?.detail ?? firstValidationError ?? body?.title ?? (error.status >= 500 ? 'The server could not complete the request.' : fallback);
}

/** All validation messages, de-duplicated, for showing under a form. */
export function validationSummary(errors: Record<string, string[]>): string {
  return [...new Set(Object.values(errors).flat())].join(' ');
}

/** Sign-in specific wording: a 401 here means bad credentials, not an expired session. */
export const loginError = (status: number): string =>
  status === 0 ? 'Cannot reach the CQ Pulse API. Make sure it is running and try again.'
  : status === 401 ? 'Invalid email or password.'
  : status === 429 ? 'Too many sign-in attempts. Please wait a moment and try again.'
  : `Sign in failed (HTTP ${status}). Please try again.`;
