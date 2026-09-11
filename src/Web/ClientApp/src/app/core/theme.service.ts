import { Injectable, signal } from '@angular/core';

export type ThemePreference = 'light' | 'dark' | 'auto';
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly preference = signal<ThemePreference>('auto');
  private readonly media = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null;
  constructor() {
    try { this.preference.set(this.parse(localStorage.getItem('picoColorScheme'))); } catch { /* Storage may be unavailable. */ }
    this.apply();
    this.media?.addEventListener('change', () => this.apply());
    if (typeof window !== 'undefined') window.addEventListener('storage', event => {
      if (event.key === 'picoColorScheme' || event.key === null) { this.preference.set(this.parse(event.newValue)); this.apply(); }
    });
  }
  select(value: string) {
    this.preference.set(this.parse(value));
    try { localStorage.setItem('picoColorScheme', this.preference()); } catch { /* Preference still works for this session. */ }
    this.apply();
  }
  private parse(value: string | null): ThemePreference { return value === 'light' || value === 'dark' ? value : 'auto'; }
  private apply() {
    if (typeof document === 'undefined') return;
    const resolved = this.preference() === 'auto' ? (this.media?.matches ? 'dark' : 'light') : this.preference();
    document.documentElement.dataset['theme'] = resolved;
    document.documentElement.style.colorScheme = resolved;
  }
}
