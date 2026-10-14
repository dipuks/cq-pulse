import { HttpErrorResponse } from '@angular/common/http';
import { loginError, problemMessage } from './errors';

const failure = (status: number, error: unknown = null) => new HttpErrorResponse({ status, error });

describe('problemMessage', () => {
  it('reports an unreachable API', () => {
    expect(problemMessage(failure(0))).toContain('Cannot reach the CQ Pulse API');
  });

  it('summarizes validation errors from Problem Details', () => {
    const message = problemMessage(failure(400, {
      title: 'One or more validation errors occurred.',
      errors: { TestCount: ['Test count must be between 1 and 50,000.'], Percentages: ['Pass, failure and skipped percentages must total 100.'] }
    }));
    expect(message).toContain('Test count must be between 1 and 50,000.');
    expect(message).toContain('must total 100');
  });

  it('uses the detail for not-found and conflict responses', () => {
    expect(problemMessage(failure(404, { title: 'Resource not found.', detail: 'The selected project does not exist.' })))
      .toBe('The selected project does not exist.');
    expect(problemMessage(failure(409, { detail: 'An active simulation already exists for this project and suite.' })))
      .toContain('active simulation');
  });

  it('does not leak server internals for 500 responses without detail', () => {
    expect(problemMessage(failure(500, 'System.InvalidOperationException at ...'))).toBe('The server could not complete the request.');
  });

  it('falls back for non-HTTP errors', () => {
    expect(problemMessage(new Error('boom'), 'Fallback')).toBe('Fallback');
  });
});

describe('loginError', () => {
  it('maps sign-in statuses', () => {
    expect(loginError(0)).toContain('Cannot reach the CQ Pulse API');
    expect(loginError(401)).toBe('Invalid email or password.');
    expect(loginError(429)).toContain('Too many sign-in attempts');
    expect(loginError(500)).toContain('HTTP 500');
  });
});
