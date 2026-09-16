import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CustomerService, Outcome } from '../core/customer.service';
import { SessionService } from '../core/session.service';
import { ICustomerResponse } from '../web-api-client';
import { OutcomeComponent, PageHeadingComponent } from '../shared/ui.components';
import { customerIdValidators } from '../shared/customer-form-rules';

@Component({
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, PageHeadingComponent, OutcomeComponent],
  templateUrl: './customer-list.component.html'
})
export class CustomerListComponent implements OnInit {
  readonly session = inject(SessionService);
  private readonly api = inject(CustomerService);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private version = 0;
  readonly form = inject(FormBuilder).nonNullable.group({ customerId: ['', customerIdValidators] });
  readonly busy = signal(false);
  readonly result = signal<Outcome<ICustomerResponse[]> | null>(null);

  ngOnInit() {
    this.destroy.onDestroy(() => ++this.version);
    void this.refresh();
  }

  async refresh() {
    const version = ++this.version;
    this.busy.set(true);
    this.result.set(null);
    const result = await this.api.list();
    if (version !== this.version) return;
    this.result.set(result);
    this.busy.set(false);
  }

  async search() {
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    await this.router.navigate(['/customers', this.form.controls.customerId.value, 'overview']);
  }
}
