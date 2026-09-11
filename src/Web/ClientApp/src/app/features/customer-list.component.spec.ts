import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { CustomerListComponent } from './customer-list.component';
import { CustomerService } from '../core/customer.service';
import { SessionService } from '../core/session.service';

describe('customer list', () => {
  let api: { list: jasmine.Spy };
  let write: boolean;
  beforeEach(() => {
    api = { list: jasmine.createSpy('list') };
    write = true;
    TestBed.configureTestingModule({
      imports: [CustomerListComponent],
      providers: [provideRouter([]), { provide: CustomerService, useValue: api },
        { provide: SessionService, useValue: { canWrite: () => write } }]
    });
  });

  async function render(result: unknown) {
    api.list.and.resolveTo(result);
    const fixture = TestBed.createComponent(CustomerListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows an empty registry and hides creation from readers', async () => {
    write = false;
    const fixture = await render({ status: 'Success', data: [] });
    expect(fixture.nativeElement.textContent).toContain('No customers yet');
    expect(fixture.nativeElement.querySelector('a[href="/customers/create"]')).toBeNull();
  });

  it('renders API names and overview links', async () => {
    const fixture = await render({ status: 'Success', data: [{ customerId: 'ID-1', displayName: 'Registered name' }] });
    expect(fixture.nativeElement.querySelector('tbody').textContent).toContain('Registered name');
    expect(fixture.nativeElement.querySelector('tbody a').getAttribute('href')).toBe('/customers/ID-1/overview');
  });

  it('distinguishes source failures from an empty registry', async () => {
    const fixture = await render({ status: 'Error', issues: [{ message: 'Source unavailable' }] });
    expect(fixture.nativeElement.textContent).toContain('Source unavailable');
    expect(fixture.nativeElement.textContent).not.toContain('No customers yet');
  });

  it('ignores superseded responses and clears old data while refreshing', async () => {
    let completeFirst!: (result: unknown) => void;
    api.list.and.returnValue(new Promise(resolve => completeFirst = resolve));
    const fixture = TestBed.createComponent(CustomerListComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading customers');
    api.list.and.resolveTo({ status: 'Success', data: [{ customerId: 'NEW', displayName: 'Latest' }] });
    await fixture.componentInstance.refresh();
    completeFirst({ status: 'Success', data: [{ customerId: 'OLD', displayName: 'Stale' }] });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Latest');
    expect(fixture.nativeElement.textContent).not.toContain('Stale');
    api.list.and.returnValue(new Promise(() => {}));
    void fixture.componentInstance.refresh();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('tbody')).toBeNull();
    fixture.destroy();
  });

  it('validates exact lookup before navigating', async () => {
    const fixture = await render({ status: 'Success', data: [] });
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture.componentInstance.form.controls.customerId.setValue('invalid_id');
    await fixture.componentInstance.search();
    expect(navigate).not.toHaveBeenCalled();
    fixture.componentInstance.form.controls.customerId.setValue('CUST-1');
    await fixture.componentInstance.search();
    expect(navigate).toHaveBeenCalledWith(['/customers', 'CUST-1', 'overview']);
  });
});
