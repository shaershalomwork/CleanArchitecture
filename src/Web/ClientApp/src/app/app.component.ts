import { Component, HostListener, OnInit, inject, signal } from '@angular/core';
import { Router, NavigationEnd } from '@angular/router';
import { SessionService } from './core/session.service';
import { ThemeService } from './core/theme.service';

@Component({ standalone: false, selector: 'app-root', templateUrl: './app.component.html' })
export class AppComponent implements OnInit {
  readonly session = inject(SessionService); readonly theme = inject(ThemeService);
  readonly menu = signal(false); readonly logoutError = signal(''); readonly signingOut = signal(false);
  readonly router = inject(Router);
  constructor() { this.router.events.subscribe(event => { if (event instanceof NavigationEnd) { this.menu.set(false); this.logoutError.set(''); document.getElementById('main-content')?.focus(); } }); }
  get customerActive() { return this.router.url.split('?')[0] === '/customers' || /^\/customers\/[^/]+\/overview/.test(this.router.url); }
  toggleMenu() {
    this.menu.set(!this.menu());
    if (this.menu()) setTimeout(() => document.querySelector<HTMLElement>('.sidebar .brand')?.focus());
    else document.querySelector<HTMLElement>('.mobile-menu')?.focus();
  }
  @HostListener('window:resize') onResize() { if (window.innerWidth >= 1024) this.menu.set(false); }
  navigationKey(event: KeyboardEvent) {
    if (!this.menu()) return;
    if (event.key === 'Escape') { event.preventDefault(); this.toggleMenu(); }
    if (event.key !== 'Tab') return;
    const items = Array.from(document.querySelectorAll<HTMLElement>('.sidebar a, .sidebar button')).filter(item => item.offsetParent !== null);
    const first = items[0], last = items[items.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }
  ngOnInit() { void this.session.load(); }
  async logout() {
    this.signingOut.set(true); this.logoutError.set('');
    try { await this.session.logout(); }
    catch { this.signingOut.set(false); this.logoutError.set('Unable to sign out. Please try again.'); }
  }
}
