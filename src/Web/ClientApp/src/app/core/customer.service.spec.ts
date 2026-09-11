import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../web-api-client';
import { CustomerService } from './customer.service';

describe('customer API reads', () => {
  let api: CustomerService, backend: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(), { provide: API_BASE_URL, useValue: '' }] });
    api = TestBed.inject(CustomerService);
    backend = TestBed.inject(HttpTestingController);
  });
  afterEach(() => backend.verify());

  it('uses the generated list API and deserializes registry records', async () => {
    const result = api.list();
    const request = backend.expectOne('/api/customers');
    expect(request.request.method).toBe('GET');
    request.flush(new Blob([JSON.stringify({ status: 'Success', data: [{ customerId: 'ID', displayName: 'Stored', observedAt: '2026-09-10T12:00:00Z' }], issues: [], correlationId: 'trace' })], { type: 'application/json' }));
    const outcome = await result;
    expect(outcome.data?.[0].displayName).toBe('Stored');
    expect(outcome.data?.[0].observedAt).toEqual(new Date('2026-09-10T12:00:00Z'));
  });

  it('preserves structured read failures without describing an unknown write', async () => {
    const result = api.list();
    backend.expectOne('/api/customers').flush(new Blob([JSON.stringify({ status: 'Error', data: null, issues: [{ code: 'CUSTOMER.UNAVAILABLE', message: 'Unavailable' }], correlationId: 'trace' })], { type: 'application/json' }), { status: 503, statusText: 'Unavailable' });
    const outcome = await result;
    expect(outcome.status).toBe('Error');
    expect(outcome.data).toBeFalsy();
    expect(outcome.issues?.[0].code).toBe('CUSTOMER.UNAVAILABLE');
  });
});
