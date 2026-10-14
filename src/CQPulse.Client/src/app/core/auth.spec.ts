import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { fakeJwt } from '../../testing/jwt';
import { AuthService, authInterceptor, safeReturnUrl, tokenExpiry } from './auth';

describe('tokenExpiry', () => {
  it('reads the exp claim from a base64url payload', () => {
    const token = fakeJwt(100);
    expect(tokenExpiry(token)).toBeGreaterThan(Date.now() / 1000);
  });

  it('returns null for tokens that are not JWTs', () => {
    expect(tokenExpiry('not-a-token')).toBeNull();
    expect(tokenExpiry('a.%%%.c')).toBeNull();
  });
});

describe('safeReturnUrl', () => {
  it('accepts in-app paths only', () => {
    expect(safeReturnUrl('/runs/1')).toBe('/runs/1');
    expect(safeReturnUrl('https://evil.example')).toBeUndefined();
    expect(safeReturnUrl('//evil.example')).toBeUndefined();
    expect(safeReturnUrl('/login?x=1')).toBeUndefined();
    expect(safeReturnUrl(null)).toBeUndefined();
  });
});

describe('AuthService and authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
  });

  afterEach(() => backend.verify());

  it('considers only unexpired tokens authenticated', () => {
    expect(auth.isAuthenticated()).toBe(false);
    auth.storeToken(fakeJwt(-10));
    expect(auth.isAuthenticated()).toBe(false);
    auth.storeToken('garbage');
    expect(auth.isAuthenticated()).toBe(false);
    auth.storeToken(fakeJwt(600));
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('adds the bearer token to API requests', () => {
    const token = fakeJwt(600);
    auth.storeToken(token);

    http.get('/api/projects').subscribe();

    expect(backend.expectOne('/api/projects').request.headers.get('Authorization')).toBe(`Bearer ${token}`);
  });

  it('does not send a token with the sign-in request', () => {
    auth.storeToken(fakeJwt(600));

    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });

    expect(backend.expectOne('/api/auth/login').request.headers.has('Authorization')).toBe(false);
  });

  it('expires the session when the API returns 401', () => {
    auth.storeToken(fakeJwt(600));
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    http.get('/api/runs').subscribe({ error: () => undefined });
    backend.expectOne('/api/runs').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(localStorage.getItem('cq-pulse-token')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.objectContaining({ queryParams: expect.objectContaining({ reason: 'expired' }) }));
  });

  it('does not treat a failed sign-in as an expired session', () => {
    const navigate = vi.spyOn(router, 'navigate');

    http.post('/api/auth/login', {}).subscribe({ error: () => undefined });
    backend.expectOne('/api/auth/login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).not.toHaveBeenCalled();
  });

  it('keeps the session on 403', () => {
    auth.storeToken(fakeJwt(600));

    http.get('/api/projects').subscribe({ error: () => undefined });
    backend.expectOne('/api/projects').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(auth.token()).not.toBeNull();
  });
});
