import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { firstValueFrom, from, switchMap, catchError, throwError } from 'rxjs';
import { AuthenticationClient, CurrentSessionResponse } from '../web-api-client';

@Injectable({ providedIn: 'root' })
export class CsrfService {
  private http = inject(HttpClient);
  private pending?: Promise<string>;
  reset() { this.pending = undefined; }
  token(): Promise<string> {
    return this.pending ??= firstValueFrom(this.http.get<{ token: string }>('/auth/antiforgery'))
      .then(result => { if (!result.token) throw new Error('No antiforgery token'); return result.token; })
      .catch(error => { this.reset(); throw error; });
  }
}

@Injectable({ providedIn: 'root' })
export class SessionService {
  private api = inject(AuthenticationClient);
  private csrf = inject(CsrfService);
  readonly user = signal<CurrentSessionResponse | null>(null);
  readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  readonly name = computed(() => this.user()?.name || this.user()?.id || 'Signed-in user');
  readonly canRead = computed(() => !!this.user()?.capabilities?.canReadCustomers);
  readonly canWrite = computed(() => !!this.user()?.capabilities?.canWriteCustomers);
  private pending?: Promise<void>;
  load(): Promise<void> {
    if (this.state() === 'ready') return Promise.resolve();
    return this.pending ??= this.refresh().finally(() => this.pending = undefined);
  }
  async refresh() {
    this.state.set('loading');
    try { this.user.set(await firstValueFrom(this.api.me())); this.state.set('ready'); }
    catch (error) {
      this.user.set(null);
      this.state.set((error as { status?: number }).status === 401 ? 'ready' : 'error');
    }
    this.csrf.reset();
  }
  clear() { this.user.set(null); this.state.set('ready'); this.csrf.reset(); }
  home() { return this.canRead() ? '/customers' : this.canWrite() ? '/customers/manage' : '/account'; }
  async logout() {
    this.csrf.reset();
    const token = await this.csrf.token();
    const form = document.createElement('form'); form.method = 'post'; form.action = '/auth/logout';
    const input = document.createElement('input'); input.type = 'hidden'; input.name = '__RequestVerificationToken'; input.value = token;
    form.append(input); document.body.append(form); form.submit();
  }
}

export const sessionGuard: CanActivateFn = async (route, state) => {
  const session = inject(SessionService), router = inject(Router);
  await session.load();
  if (!session.user()) return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
  const permission = route.data['permission'];
  if ((permission === 'read' && !session.canRead()) || (permission === 'write' && !session.canWrite()))
    return router.createUrlTree(['/forbidden']);
  return true;
};

export const csrfInterceptor: HttpInterceptorFn = (request, next) => {
  const csrf = inject(CsrfService), session = inject(SessionService), router = inject(Router);
  const url = new URL(request.url, window.location.origin);
  if (url.origin !== window.location.origin) return next(request);
  const mutation = !['GET', 'HEAD', 'OPTIONS'].includes(request.method);
  const response = mutation ? from(csrf.token()).pipe(switchMap(token => next(request.clone({ setHeaders: { 'X-CSRF-TOKEN': token } })))) : next(request);
  return response.pipe(catchError((error: HttpErrorResponse) => {
    if (error.status === 401 && !url.pathname.startsWith('/auth/')) {
      session.clear();
      void router.navigate(['/sign-in'], { queryParams: { expired: '1', returnUrl: router.url } });
    }
    if (error.status === 400) csrf.reset();
    return throwError(() => error);
  }));
};
