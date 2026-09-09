import { Component, OnInit, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CustomersClient, ApiOperationResponseOfCustomerOverviewResponse } from './web-api-client';

@Component({ standalone: false, selector: 'app-root', templateUrl: './app.component.html' })
export class AppComponent implements OnInit {
  readonly name = signal<string | null>(null);
  customerId = 'CUST-001';
  readonly busy = signal(false);
  readonly result = signal<ApiOperationResponseOfCustomerOverviewResponse | null>(null);
  readonly connectionError = signal('');
  constructor(private customers: CustomersClient) {}
  async ngOnInit() {
    try {
      const response = await fetch('/auth/me');
      this.name.set(response.ok ? (await response.json()).name : null);
    } catch { this.name.set(null); }
  }
  async load() {
    this.busy.set(true); this.result.set(null); this.connectionError.set('');
    try { this.result.set(await firstValueFrom(this.customers.getCustomerOverview(this.customerId))); }
    catch (error: unknown) {
      if (error instanceof ApiOperationResponseOfCustomerOverviewResponse) this.result.set(error);
      else this.connectionError.set('Unable to reach the service. Please try again.');
    } finally { this.busy.set(false); }
  }
  async logout() {
    const response = await fetch('/auth/antiforgery');
    if (!response.ok) return;
    const { token } = await response.json();
    const form = document.createElement('form'); form.method = 'post'; form.action = '/auth/logout';
    const input = document.createElement('input'); input.type = 'hidden'; input.name = '__RequestVerificationToken'; input.value = token;
    form.append(input); document.body.append(form); form.submit();
  }
}
