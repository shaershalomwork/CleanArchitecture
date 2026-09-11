import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { FormControl } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { SessionService, CsrfService, csrfInterceptor } from './session.service';
import { ThemeService } from './theme.service';
import { AuthenticationClient, CurrentSessionResponse } from '../web-api-client';
import { customerIdValidators, displayNameValidators } from '../shared/customer-form-rules';

describe('session', () => {
  const api = { me: jasmine.createSpy('me') };
  beforeEach(() => { api.me.calls.reset(); TestBed.configureTestingModule({ providers: [{ provide: AuthenticationClient, useValue: api }, { provide: CsrfService, useValue: { reset() {} } }] }); });
  it('recognizes an authenticated identity with no display name', async () => {
    api.me.and.returnValue(of(CurrentSessionResponse.fromJS({ id: 'writer', name: null, capabilities: { canReadCustomers: false, canWriteCustomers: true } })));
    const session = TestBed.inject(SessionService); await session.load();
    expect(session.user()).not.toBeNull(); expect(session.name()).toBe('writer'); expect(session.home()).toBe('/customers/manage');
  });
  it('does not turn roles or raw permissions into capabilities', async () => {
    api.me.and.returnValue(of(CurrentSessionResponse.fromJS({ roles: ['admin'], permissions: ['customers.write'], capabilities: { canReadCustomers: false, canWriteCustomers: false } })));
    const session = TestBed.inject(SessionService); await session.load();
    expect(session.canWrite()).toBeFalse(); expect(session.home()).toBe('/account');
  });
  it('distinguishes an anonymous session from a service failure', async () => {
    const session = TestBed.inject(SessionService);
    api.me.and.returnValue(throwError(() => ({ status: 401 }))); await session.refresh(); expect(session.state()).toBe('ready');
    api.me.and.returnValue(throwError(() => ({ status: 503 }))); await session.refresh(); expect(session.state()).toBe('error');
  });
});

describe('antiforgery transport', () => {
  let http: HttpClient, backend: HttpTestingController;
  const session = { clear: jasmine.createSpy('clear') };
  const router = { url: '/customers/manage', navigate: jasmine.createSpy('navigate') };
  beforeEach(() => {
    session.clear.calls.reset(); router.navigate.calls.reset();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([csrfInterceptor])), provideHttpClientTesting(), { provide: SessionService, useValue: session }, { provide: Router, useValue: router }] });
    http = TestBed.inject(HttpClient); backend = TestBed.inject(HttpTestingController);
  });
  afterEach(() => backend.verify());
  it('shares token acquisition and protects every mutation', fakeAsync(() => {
    http.post('/api/customers', {}).subscribe(); http.patch('/api/customers/CUST-001', {}).subscribe();
    backend.expectOne('/auth/antiforgery').flush({ token: 'paired-token' }); tick();
    const writes = backend.match(request => request.url.startsWith('/api/'));
    expect(writes.length).toBe(2); writes.forEach(request => { expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('paired-token'); request.flush({}); });
  }));
  it('does not send a CSRF token to another origin or on reads', () => {
    http.post('https://other.example/api', {}).subscribe(); http.get('/api/customers/CUST-001/overview').subscribe();
    backend.match(() => true).forEach(request => { expect(request.request.headers.has('X-CSRF-TOKEN')).toBeFalse(); request.flush({}); });
    backend.expectNone('/auth/antiforgery');
  });
  it('does not replay a failed write', fakeAsync(() => {
    http.delete('/api/customers/CUST-001').subscribe({ error: () => {} });
    backend.expectOne('/auth/antiforgery').flush({ token: 'paired-token' }); tick();
    backend.expectOne('/api/customers/CUST-001').flush({}, { status: 502, statusText: 'Unknown outcome' }); tick();
    expect(backend.match('/api/customers/CUST-001').length).toBe(0);
  }));
  it('clears an expired session without retrying the request', () => {
    http.get('/api/customers/CUST-001/overview').subscribe({ error: () => {} });
    backend.expectOne('/api/customers/CUST-001/overview').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(session.clear).toHaveBeenCalled(); expect(router.navigate).toHaveBeenCalled();
  });
});

describe('theme preferences', () => {
  let previous: string | null;
  let listener: () => void; let media: { matches: boolean; addEventListener: (name: string, callback: () => void) => void };
  beforeEach(() => { previous = localStorage.getItem('picoColorScheme'); localStorage.removeItem('picoColorScheme'); media = { matches: true, addEventListener: (_, callback) => listener = callback }; spyOn(window, 'matchMedia').and.returnValue(media as MediaQueryList); });
  afterEach(() => { if (previous === null) localStorage.removeItem('picoColorScheme'); else localStorage.setItem('picoColorScheme', previous); });
  it('follows system appearance until a user overrides it', () => {
    const theme = new ThemeService(); expect(document.documentElement.dataset['theme']).toBe('dark');
    theme.select('light'); listener(); expect(document.documentElement.dataset['theme']).toBe('light');
    theme.select('auto'); expect(document.documentElement.dataset['theme']).toBe('dark');
    media.matches = false; listener(); expect(document.documentElement.dataset['theme']).toBe('light');
  });
  it('restores a persisted selection before rendering', () => { localStorage.setItem('picoColorScheme', 'dark'); const theme = new ThemeService(); expect(theme.preference()).toBe('dark'); expect(document.documentElement.dataset['theme']).toBe('dark'); });
  it('survives unavailable storage', () => { spyOn(Storage.prototype, 'getItem').and.throwError('Blocked'); spyOn(Storage.prototype, 'setItem').and.throwError('Blocked'); const theme = new ThemeService(); expect(() => theme.select('light')).not.toThrow(); expect(document.documentElement.dataset['theme']).toBe('light'); });
});

describe('customer form constraints', () => {
  it('matches the server ID constraints', () => { for (const value of ['', 'bad_id', 'a'.repeat(51)]) expect(new FormControl(value, customerIdValidators).invalid).toBeTrue(); expect(new FormControl('CUST-001', customerIdValidators).valid).toBeTrue(); });
  it('rejects blank and oversized names while allowing Unicode', () => { for (const value of ['', '   ', 'n'.repeat(201)]) expect(new FormControl(value, displayNameValidators).invalid).toBeTrue(); expect(new FormControl('שלום', displayNameValidators).valid).toBeTrue(); });
});
