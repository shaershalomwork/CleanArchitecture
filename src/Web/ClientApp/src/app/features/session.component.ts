import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { SessionService } from '../core/session.service';
import { ThemeService } from '../core/theme.service';
import { AuthenticationClient, AuthenticationUiOptionsResponse } from '../web-api-client';
import { PageHeadingComponent } from '../shared/ui.components';

@Component({ standalone: true, imports: [CommonModule, RouterLink], template: `
  <div class="sign-in-layout"><section class="welcome"><p class="eyebrow">CUSTOMER SERVICES</p><h1>Clarity for every<br />customer interaction.</h1><p>Find the information you need.<br />Make the right update. Keep moving.</p><div class="welcome-lines" aria-hidden="true"><span></span><span></span><span></span></div><p class="footnote">One workspace for customer profiles and billing insights.</p></section>
  <section class="panel sign-in-card"><span class="brand-mark" aria-hidden="true">C</span><h2>Welcome back</h2><p class="muted">Sign in to your customer workspace.</p>
    <p *ngIf="expired" class="note" role="status">Your session has expired. Sign in to continue.</p>
    <p *ngIf="failed" class="outcome error" role="alert">Sign-in could not be completed. Please try again.</p>
    <p *ngIf="loading()" role="status" aria-busy="true">Loading sign-in options…</p>
    <div *ngIf="error()" class="outcome error" role="alert"><p>Unable to load sign-in options.</p><button class="secondary" (click)="load()">Try again</button></div>
    <ng-container *ngIf="options() as available">
      <div *ngIf="available.developmentProfiles?.length" class="demo-notice"><span class="badge caution">Development</span><p>Choose a demo access profile. These profiles are available only in local development.</p><label for="profile">Access profile</label><select id="profile" (change)="profile.set($any($event.target).value)"><option *ngFor="let item of available.developmentProfiles" [value]="item.id">{{item.label}}</option></select></div>
      <a *ngIf="available.loginAvailable" class="button full-width" [href]="loginUrl()">Sign in <span aria-hidden="true">→</span></a>
      <p *ngIf="!available.loginAvailable" class="note">This deployment uses Bearer tokens. Open the API reference to send authenticated requests.</p>
    </ng-container><a *ngIf="session.user()" class="text-link" [routerLink]="session.home()">Return to workspace</a><p class="footnote">Access is determined by your assigned permissions.</p>
  </section></div>` })
export class SignInComponent implements OnInit {
  readonly session = inject(SessionService); private api = inject(AuthenticationClient); private route = inject(ActivatedRoute);
  readonly profile = signal('reader'); readonly options = signal<AuthenticationUiOptionsResponse | null>(null);
  readonly loading = signal(true); readonly error = signal(false);
  readonly expired = this.route.snapshot.queryParamMap.has('expired'); readonly failed = this.route.snapshot.queryParamMap.has('error');
  ngOnInit() { void this.load(); }
  async load() {
    this.loading.set(true); this.error.set(false);
    try { this.options.set(await firstValueFrom(this.api.getAuthenticationOptions())); }
    catch { this.error.set(true); }
    finally { this.loading.set(false); }
  }
  loginUrl() {
    const candidate = this.route.snapshot.queryParamMap.get('returnUrl') || '/';
    const returnUrl = candidate.startsWith('/') && !candidate.startsWith('//') && !candidate.includes('\\') ? candidate : '/';
    return '/auth/login?returnUrl=' + encodeURIComponent(returnUrl) + (this.options()?.developmentProfiles?.length ? '&profile=' + encodeURIComponent(this.profile()) : '');
  }
}

@Component({ standalone: true, imports: [CommonModule, PageHeadingComponent], template: `
  <app-page-heading title="Your account" description="Your identity, access, and workspace preferences." />
  <div class="detail-grid"><section class="panel"><span class="eyebrow">SIGNED IN AS</span><h2>{{session.name()}}</h2><p class="mono muted">{{session.user()?.id || 'No subject supplied'}}</p><hr />
    <h3>Customer access</h3><div class="access-list"><p><span class="access-dot" [class.enabled]="session.canRead()"></span>View customers <strong>{{session.canRead() ? 'Allowed' : 'Not assigned'}}</strong></p><p><span class="access-dot" [class.enabled]="session.canWrite()"></span>Manage customers <strong>{{session.canWrite() ? 'Allowed' : 'Not assigned'}}</strong></p></div>
    <p *ngIf="!session.canRead() && !session.canWrite()" class="note">You are signed in, but no customer permissions have been assigned. Contact your access administrator if you need access.</p>
    <h3>Roles</h3><p class="muted" *ngIf="!session.user()?.roles?.length">No roles supplied by your identity provider.</p><span class="badge" *ngFor="let role of session.user()?.roles">{{role}}</span>
    <h3>Permissions</h3><p class="muted" *ngIf="!session.user()?.permissions?.length">No permissions assigned.</p><span class="badge mono" *ngFor="let permission of session.user()?.permissions">{{permission}}</span>
  </section><section class="panel"><span class="eyebrow">MAKE IT YOURS</span><h2>Appearance</h2><p class="muted">Choose a theme that works for you.</p><label for="theme">Color theme</label><select id="theme" [value]="theme.preference()" (change)="theme.select($any($event.target).value)"><option value="auto">System preference</option><option value="light">Light</option><option value="dark">Dark</option></select><p class="footnote">Your selection is remembered on this browser. System follows your device's appearance.</p></section></div>` })
export class AccountComponent { readonly session = inject(SessionService); readonly theme = inject(ThemeService); }

interface Probe { label: string; route: string; description: string; value: string; }
@Component({ standalone: true, imports: [CommonModule, PageHeadingComponent], template: `
  <app-page-heading title="Service status" description="Availability of the application and its connected services."><button class="secondary" (click)="refresh()" [disabled]="busy()" [attr.aria-busy]="busy()">Refresh status</button></app-page-heading>
  <div class="detail-grid"><section class="panel" *ngFor="let probe of probes()"><div class="card-header"><span class="eyebrow">{{probe.label}}</span><span class="badge" [class.caution]="probe.value !== 'Healthy'">{{busy() ? 'Checking' : probe.value}}</span></div><h2>{{probe.label === 'APPLICATION' ? 'Application availability' : 'Source readiness'}}</h2><p class="muted">{{probe.description}}</p><p class="status-value" [class.field-error]="probe.value === 'Unhealthy'">{{busy() ? 'Checking…' : probe.value}}</p></section></div>
  <p class="footnote" role="status">{{checkedAt() ? 'Last checked ' + (checkedAt() | date:'medium') : 'Status is checked on request.'}} · These probes show aggregate status. Restricted probes may be unavailable.</p>` })
export class StatusComponent implements OnInit {
  private http = inject(HttpClient); readonly busy = signal(false); readonly checkedAt = signal<Date | null>(null);
  readonly probes = signal<Probe[]>([{ label: 'APPLICATION', route: '/alive', description: 'Confirms the application is running, independently of external sources.', value: 'Not checked' }, { label: 'CONNECTED SERVICES', route: '/health', description: 'Checks the customer registry and optional billing connection.', value: 'Not checked' }]);
  ngOnInit() { void this.refresh(); }
  async refresh() {
    if (this.busy()) return; this.busy.set(true);
    const values = await Promise.all(this.probes().map(async probe => {
      let body: string;
      try { body = await firstValueFrom(this.http.get(probe.route, { responseType: 'text' })); }
      catch (error) { body = typeof (error as HttpErrorResponse).error === 'string' ? (error as HttpErrorResponse).error : ''; }
      return { ...probe, value: ['Healthy', 'Degraded', 'Unhealthy'].includes(body.trim()) ? body.trim() : 'Unavailable' };
    }));
    this.probes.set(values); this.checkedAt.set(new Date()); this.busy.set(false);
  }
}

@Component({ standalone: true, imports: [RouterLink], template: `<section class="panel empty-state"><span class="empty-icon" aria-hidden="true">⊘</span><h1>{{forbidden ? 'Access not available' : 'Page not found'}}</h1><p>{{forbidden ? 'Your current permissions do not allow this action.' : 'This page may have moved, or the address may be incorrect.'}}</p><a class="button" [routerLink]="session.home()">Return to workspace</a></section>` })
export class UnavailableComponent { readonly session = inject(SessionService); readonly forbidden = inject(ActivatedRoute).snapshot.data['forbidden']; }

@Component({ standalone: true, template: `<p role="status">Opening your workspace…</p>` })
export class LandingComponent implements OnInit {
  private session = inject(SessionService); private router = inject(Router);
  ngOnInit() { void this.router.navigateByUrl(this.session.home(), { replaceUrl: true }); }
}
