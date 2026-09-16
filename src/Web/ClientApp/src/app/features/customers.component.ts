import { Component, DestroyRef, OnInit, ViewChild, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CustomerService, Outcome } from '../core/customer.service';
import { ICustomerOverviewResponse } from '../web-api-client';
import { SessionService } from '../core/session.service';
import { ConfirmComponent, OutcomeComponent, PageHeadingComponent } from '../shared/ui.components';
import { customerIdValidators, displayNameValidators } from '../shared/customer-form-rules';

@Component({ standalone: true, imports: [CommonModule, ReactiveFormsModule, RouterLink, PageHeadingComponent, OutcomeComponent], templateUrl: './customers.component.html' })
export class CustomersComponent implements OnInit {
  readonly session = inject(SessionService);
  private api = inject(CustomerService); private router = inject(Router); private route = inject(ActivatedRoute);
  private destroy = inject(DestroyRef); private version = 0;
  readonly form = inject(FormBuilder).nonNullable.group({ customerId: ['', customerIdValidators] });
  readonly busy = signal(false); readonly result = signal<Outcome<ICustomerOverviewResponse> | null>(null);
  readonly activeId = signal('');
  ngOnInit() {
    this.destroy.onDestroy(() => this.version++);
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroy)).subscribe(params => {
      const id = params.get('customerId');
      if (id) { this.form.controls.customerId.setValue(id); void this.load(id); }
    });
  }
  async search() {
    this.form.markAllAsTouched(); if (this.form.invalid || this.busy()) return;
    const id = this.form.controls.customerId.value;
    if (id === this.activeId()) await this.load(id);
    else await this.router.navigate(['/customers', id, 'overview']);
  }
  private async load(id: string) {
    const version = ++this.version;
    this.activeId.set(id);
    this.result.set(null);
    this.busy.set(false);
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.busy.set(true);
    const result = await this.api.lookup(id);
    if (version === this.version) { this.result.set(result); this.busy.set(false); }
  }
}

@Component({ standalone: true, imports: [CommonModule, ReactiveFormsModule, RouterLink, PageHeadingComponent, OutcomeComponent, ConfirmComponent], templateUrl: './customer-editor.component.html' })
export class CustomerEditorComponent implements OnInit {
  readonly session = inject(SessionService); private api = inject(CustomerService); private route = inject(ActivatedRoute);
  readonly create = this.route.snapshot.data['create'] === true;
  readonly mode = signal<'rename' | 'replace' | 'delete'>('rename');
  readonly form = inject(FormBuilder).nonNullable.group({ customerId: ['', customerIdValidators], displayName: ['', displayNameValidators] });
  readonly busy = signal(false); readonly result = signal<Outcome | null>(null);
  readonly receiptId = signal('');
  @ViewChild(ConfirmComponent) confirmation!: ConfirmComponent;
  ngOnInit() {
    const params = this.route.snapshot.queryParamMap;
    this.form.controls.customerId.setValue(params.get('customerId') || '');
    const mode = params.get('mode'); if (mode === 'replace' || mode === 'delete') this.setMode(mode);
  }
  setMode(mode: 'rename' | 'replace' | 'delete') {
    if (this.busy()) return;
    this.mode.set(mode); this.result.set(null);
    this.form.controls.displayName.setValidators(mode === 'delete' ? [] : displayNameValidators);
    this.form.controls.displayName.updateValueAndValidity();
  }
  async submit() {
    this.form.markAllAsTouched(); if (this.form.invalid || this.busy()) return;
    if (!this.create && this.mode() === 'delete') { this.confirmation.open(); return; }
    await this.save();
  }
  async save() {
    if (this.busy() || this.form.invalid) return;
    const { customerId, displayName } = this.form.getRawValue();
    this.busy.set(true); this.result.set(null); this.receiptId.set(customerId);
    const outcome = this.create ? await this.api.create(customerId, displayName) : this.mode() === 'delete' ? await this.api.delete(customerId)
      : this.mode() === 'replace' ? await this.api.replace(customerId, displayName) : await this.api.rename(customerId, displayName);
    this.result.set(outcome); this.busy.set(false);
    for (const issue of outcome.issues || []) {
      const key = issue.target?.split('.').pop()?.toLowerCase();
      const control = key === 'customerid' ? this.form.controls.customerId : key === 'displayname' ? this.form.controls.displayName : null;
      if (control) { control.setErrors({ server: issue.message }); control.markAsTouched(); }
    }
    if (outcome.status === 'Success') this.form.markAsPristine();
  }
  get successTitle() { return this.create ? 'Customer created' : this.mode() === 'delete' ? 'Customer deleted' : 'Customer updated'; }
}
