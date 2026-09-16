import { Injectable, inject } from '@angular/core';
import { firstValueFrom, Observable } from 'rxjs';
import { CustomersClient, CreateCustomerRequest, ReplaceCustomerRequest, PatchCustomerRequest, ICustomerResponse, ApiIssue } from '../web-api-client';

export interface Outcome<TData = Partial<ICustomerResponse>> {
  status?: string;
  data?: TData;
  issues?: Partial<Pick<ApiIssue, 'code' | 'message' | 'target' | 'category'>>[];
  correlationId?: string;
}
@Injectable({ providedIn: 'root' })
export class CustomerService {
  private api = inject(CustomersClient);
  list() { return this.result(this.api.getCustomers()); }
  lookup(id: string) { return this.result(this.api.getCustomerOverview(id)); }
  create(id: string, name: string) { return this.result(this.api.createCustomer(new CreateCustomerRequest({ customerId: id, displayName: name })), true); }
  replace(id: string, name: string) { return this.result(this.api.replaceCustomer(id, new ReplaceCustomerRequest({ displayName: name })), true); }
  rename(id: string, name: string) { return this.result(this.api.patchCustomer(id, new PatchCustomerRequest({ displayName: name })), true); }
  delete(id: string) { return this.result(this.api.deleteCustomer(id), true); }
  private async result<TData>(request: Observable<Outcome<TData>>, mutation = false): Promise<Outcome<TData>> {
    try { return await firstValueFrom(request); }
    catch (error) {
      if ((error as Outcome<TData>)?.status === 'Error') return error as Outcome<TData>;
      const status = (error as { status?: number })?.status;
      if (mutation && (!status || status >= 500)) return { status: 'Error', issues: [{ code: 'CUSTOMER.OUTCOME_UNKNOWN', message: 'The service response was lost. The change may already have been saved.' }] };
      return { status: 'Error', issues: [{ code: status === 403 ? 'AUTH.FORBIDDEN' : status === 401 ? 'AUTH.UNAUTHENTICATED' : 'CONNECTION.FAILED',
        message: status === 403 ? 'You do not have permission to perform this action.' : status === 401 ? 'Your session has expired. Please sign in again.' : 'Unable to reach the service. Check your connection and try again.' }] };
    }
  }
}
