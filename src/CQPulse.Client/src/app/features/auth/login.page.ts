import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService, safeReturnUrl } from '../../core/auth';
import { loginError } from '../../core/errors';

@Component({
  imports: [FormsModule],
  template: `
    <main class="login-page">
      <form class="login-card" (ngSubmit)="login()">
        <span class="brand"><span>CQ</span> Pulse<small>CLOUD QA</small></span>
        <p class="eyebrow">SECURE WORKSPACE</p>
        <h1>Welcome back</h1>
        <p class="intro">Sign in to manage quality operations.</p>
        @if (expired) { <p class="error" role="status">Your session has expired. Please sign in again.</p> }
        <label>Email<input [(ngModel)]="email" name="email" type="email" autocomplete="username" required></label>
        <label>Password<input [(ngModel)]="password" name="password" type="password" autocomplete="current-password" required></label>
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <button class="primary wide" type="submit" [disabled]="busy() || !email || !password">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
      </form>
    </main>`
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly query = inject(ActivatedRoute).snapshot.queryParamMap;

  protected email = '';
  protected password = '';
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly expired = this.query.get('reason') === 'expired';

  protected login(): void {
    this.busy.set(true);
    this.error.set('');
    this.auth.login(this.email.trim(), this.password).subscribe({
      next: response => {
        this.auth.storeToken(response.accessToken);
        void this.router.navigateByUrl(safeReturnUrl(this.query.get('returnUrl')) ?? '/dashboard');
      },
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(loginError(e.status));
      }
    });
  }
}
