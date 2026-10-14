import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';
import { loginError } from './pages';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders only a router outlet at the root so each route chooses its layout', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
    expect(compiled.querySelector('.sidebar')).toBeNull();
  });
});

describe('Authentication routing', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()]
    });
  });

  it('renders /login without the authenticated shell', async () => {
    const harness = await RouterTestingHarness.create('/login');
    const root = harness.fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.login-card')).toBeTruthy();
    expect(root.querySelector('.sidebar')).toBeNull();
    expect(root.textContent).not.toContain('Sign out');
  });

  it('redirects protected routes to /login when there is no token', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/settings');
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('renders protected routes inside the shell when a token is present', async () => {
    localStorage.setItem('cq-pulse-token', 'header.payload.signature');
    const harness = await RouterTestingHarness.create('/settings');
    const root = harness.fixture.nativeElement as HTMLElement;
    expect(TestBed.inject(Router).url).toBe('/settings');
    expect(root.querySelector('.sidebar nav a[href="/projects"]')).toBeTruthy();
    expect(root.textContent).toContain('Sign out');
    expect(root.querySelector('.sidebar nav a.active')?.getAttribute('href')).toBe('/settings');
  });
});

describe('loginError', () => {
  it('reports an unreachable API instead of blaming credentials', () => {
    expect(loginError(0)).toContain('Cannot reach the CQ Pulse API');
  });

  it('reports rejected credentials for 401', () => {
    expect(loginError(401)).toBe('Invalid email or password.');
  });

  it('includes the status for other failures', () => {
    expect(loginError(500)).toContain('HTTP 500');
  });
});
