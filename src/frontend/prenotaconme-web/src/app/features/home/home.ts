import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';

type ApiStatus = 'checking' | 'online' | 'offline';

@Component({
  selector: 'app-home',
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    <section class="hero">
      <h1>{{ 'home.title' | transloco }}</h1>
      <p class="lead">{{ 'home.subtitle' | transloco }}</p>
      <div class="actions">
        <a mat-flat-button routerLink="/registra-attivita">{{ 'home.cta' | transloco }}</a>
        <a mat-button routerLink="/login">{{ 'home.signIn' | transloco }}</a>
      </div>
    </section>

    <ul class="points">
      @for (p of points; track p.key) {
        <li>
          <mat-icon>{{ p.icon }}</mat-icon>
          <div>
            <strong>{{ 'home.points.' + p.key + '.title' | transloco }}</strong>
            <span class="muted">{{ 'home.points.' + p.key + '.text' | transloco }}</span>
          </div>
        </li>
      }
    </ul>

    <p class="status muted">
      {{ 'home.apiStatus' | transloco }}: {{ 'home.status.' + status() | transloco }}
      @if (version()) { (v{{ version() }}) }
    </p>
  `,
  styles: `
    :host { display: block; max-width: 720px; margin: 0 auto; }
    .hero { padding: var(--space-7) 0 var(--space-6); }
    h1 { font: var(--mat-sys-display-small); font-weight: 700; letter-spacing: -0.02em; margin: 0 0 var(--space-4); }
    .lead { font: var(--mat-sys-title-medium); font-weight: 400; color: var(--mat-sys-on-surface-variant); max-width: 52ch; margin: 0; }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-5); }
    .actions a[mat-flat-button] { height: 48px; padding: 0 var(--space-5); }
    .points { list-style: none; margin: 0; padding: var(--space-5) 0; display: grid; gap: var(--space-5); border-top: 1px solid var(--mat-sys-outline-variant); }
    .points li { display: flex; gap: var(--space-4); align-items: flex-start; }
    .points mat-icon { color: var(--mat-sys-primary); flex: 0 0 auto; }
    .points div { display: grid; gap: 2px; }
    .points strong { font-weight: 600; }
    .status { font: var(--mat-sys-body-small); margin-top: var(--space-6); }
  `,
})
export class Home implements OnInit {
  private readonly api = inject(API_CLIENT);

  protected readonly points = [
    { key: 'link', icon: 'link' },
    { key: 'calendar', icon: 'event_available' },
    { key: 'mail', icon: 'mark_email_read' },
  ];
  protected readonly status = signal<ApiStatus>('checking');
  protected readonly version = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const { data } = await this.api.GET('/api/info');
      this.version.set(data?.version ?? null);
      this.status.set(data ? 'online' : 'offline');
    } catch {
      this.status.set('offline');
    }
  }
}
