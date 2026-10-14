import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { App } from './app';
import { routes } from './app.routes';
import { fakeJwt } from '../testing/jwt';

describe('App', () => {
  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
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

  it('redirects protected routes to /login with a return URL when there is no token', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/settings');
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fsettings');
  });

  it('treats an expired token as signed out and explains why', async () => {
    localStorage.setItem('cq-pulse-token', fakeJwt(-60));
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/settings');
    const root = harness.fixture.nativeElement as HTMLElement;
    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Fsettings&reason=expired');
    expect(root.textContent).toContain('Your session has expired');
  });

  it('renders protected routes inside the shell when a valid token is present', async () => {
    localStorage.setItem('cq-pulse-token', fakeJwt(3600));
    const harness = await RouterTestingHarness.create('/settings');
    const root = harness.fixture.nativeElement as HTMLElement;
    expect(TestBed.inject(Router).url).toBe('/settings');
    expect(root.querySelector('.sidebar nav a[href="/projects"]')).toBeTruthy();
    expect(root.textContent).toContain('Sign out');
    expect(root.querySelector('.sidebar nav a.active')?.getAttribute('href')).toBe('/settings');
  });
});
