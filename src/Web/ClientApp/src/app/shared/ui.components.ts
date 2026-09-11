import { Component, Input, ViewChild, ElementRef, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Outcome } from '../core/customer.service';

@Component({ selector: 'app-page-heading', standalone: true, template: `<header class="page-heading"><div><p class="eyebrow">{{eyebrow}}</p><h1>{{title}}</h1><p class="lede">{{description}}</p></div><div class="page-actions"><ng-content /></div></header>` })
export class PageHeadingComponent {
  @Input() eyebrow = 'CUSTOMER SERVICES'; @Input() title = ''; @Input() description = '';
}
@Component({ selector: 'app-outcome', standalone: true, imports: [CommonModule, RouterLink], template: `
  <section *ngIf="value" class="outcome" [class.warning]="value.status === 'Warning'" [class.error]="value.status === 'Error'" [class.success]="value.status === 'Success'" aria-live="polite">
    <h2>{{value.status === 'Success' ? successTitle : value.status === 'Warning' ? 'Partial data' : 'Unable to complete request'}}</h2>
    <p *ngFor="let issue of value.issues">{{issue.message}} <span class="issue-code">{{issue.code}}</span></p>
    <p *ngIf="unknown">The change may already have been saved. Check its status before making another attempt.</p>
    <a *ngIf="unknown && canRead && customerId" [routerLink]="['/customers', customerId, 'overview']">Check customer status</a>
    <p *ngIf="unknown && !canRead">Ask an authorized operator to check the customer in the system of record.</p>
    <small *ngIf="value.correlationId">Reference: <span class="mono">{{value.correlationId}}</span></small>
  </section>` })
export class OutcomeComponent {
  @Input() value: Outcome<unknown> | null = null; @Input() successTitle = 'Complete'; @Input() canRead = false; @Input() customerId = '';
  get unknown() { return this.value?.issues?.some(issue => issue.code === 'CUSTOMER.OUTCOME_UNKNOWN'); }
}
@Component({ selector: 'app-confirm', standalone: true, template: `
  <dialog #dialog aria-labelledby="confirm-title" (close)="closed.emit()">
    <article><p class="eyebrow">PLEASE CONFIRM</p><h2 id="confirm-title">Delete customer?</h2>
      <p>Customer <strong class="mono">{{customerId}}</strong> will be deleted. This does not change their billing records.</p>
      <p>This action cannot be undone.</p><footer class="button-row"><button autofocus class="secondary" (click)="cancel()">Cancel</button>
      <button class="danger" (click)="confirm()">Delete customer</button></footer></article>
  </dialog>` })
export class ConfirmComponent {
  @ViewChild('dialog') dialog!: ElementRef<HTMLDialogElement>;
  @Input() customerId = ''; @Output() confirmed = new EventEmitter<void>(); @Output() closed = new EventEmitter<void>();
  open() { this.dialog.nativeElement.showModal(); }
  cancel() { this.dialog.nativeElement.close(); }
  confirm() { this.cancel(); this.confirmed.emit(); }
}
