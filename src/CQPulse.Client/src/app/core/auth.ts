import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { API_BASE_URL } from './api-config';

const TOKEN_KEY = 'cq-pulse-token';

export interface LoginResponse { accessToken: string; expiresAtUtc: string; user: { id: string; email: string; displayName?: string | null }; }

/** Reads the `exp` claim (seconds since epoch). Returns null when the token is not a readable JWT. */
export function tokenExpiry(token: string): number | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;
    const json = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/')));
    return typeof json.exp === 'number' ? json.exp : null;
  } catch {
    return null;
  }
}

/**
 * Holds the JWT. It is stored in localStorage so a refresh keeps the session; the API remains the authority
 * (every request is validated server-side) and the client only uses `exp` to avoid showing a dead session.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly baseUrl = inject(API_BASE_URL);

  token(): string | null {
    try { return localStorage.getItem(TOKEN_KEY); } catch { return null; }
  }

  /** True when a token exists and has not expired (with a small clock-skew margin). */
  isAuthenticated(now = Date.now()): boolean {
    const token = this.token();
    if (!token) return false;
    const exp = tokenExpiry(token);
    return exp !== null && exp * 1000 > now + 5_000;
  }

  login(email: string, password: string) {
    return this.http.post<LoginResponse>(`${this.baseUrl}/api/auth/login`, { email, password });
  }

  storeToken(token: string): void { localStorage.setItem(TOKEN_KEY, token); }

  signOut(): void {
    this.clear();
    void this.router.navigate(['/login']);
  }

  /** Called when the API rejects the token; returns the user to sign-in and back to where they were. */
  expire(returnUrl = this.router.url): void {
    this.clear();
    void this.router.navigate(['/login'], { queryParams: { reason: 'expired', returnUrl: safeReturnUrl(returnUrl) } });
  }

  private clear(): void {
    try { localStorage.removeItem(TOKEN_KEY); } catch { /* storage unavailable */ }
  }
}

/** Only same-app relative paths are accepted as return URLs (prevents open redirects). */
export function safeReturnUrl(url: string | null | undefined): string | undefined {
  return url && url.startsWith('/') && !url.startsWith('//') && !url.startsWith('/login') ? url : undefined;
}

/** Adds the bearer token to API calls and turns a 401 on any call except sign-in into a session expiry. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const token = auth.token();
  const isLogin = request.url.endsWith('/api/auth/login');
  const authorized = token && !isLogin ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  return next(authorized).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401 && !isLogin) auth.expire();
    return throwError(() => error);
  }));
};

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isAuthenticated()) return true;
  const hadToken = auth.token() !== null;
  return inject(Router).createUrlTree(['/login'], {
    queryParams: { returnUrl: safeReturnUrl(state.url), ...(hadToken ? { reason: 'expired' } : {}) }
  });
};
