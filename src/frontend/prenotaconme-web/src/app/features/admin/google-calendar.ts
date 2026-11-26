import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../core/api/api-client';
import { Notifier } from '../../shared/api-errors';

/**
 * F2-07: the signed-in staff member connects their own Google Calendar (appointments go there, personal events
 * block free times). Shown only when the platform has Google configured, the business turned the sync on and the
 * user is a staff member of this business.
 */
@Component({
  selector: 'app-google-calendar',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    @if (status(); as s) {
      @if (s.configured && s.enabledByBusiness && s.hasStaffProfile) {
        <section class="gcal" [class.on]="s.connected && !s.error">
          <mat-icon class="lead" aria-hidden="true">event_available</mat-icon>
          <div class="text">
            <strong>Google Calendar</strong>
            <span class="muted">
              @if (s.connected && s.error) {
                {{ 'googleCalendar.revoked' | transloco }}
              } @else if (s.connected) {
                {{ 'googleCalendar.connected' | transloco: { email: s.googleEmail ?? '' } }}
              } @else {
                {{ 'googleCalendar.hint' | transloco }}
              }
            </span>
          </div>
          @if (s.connected && !s.error) {
            <button mat-button (click)="disconnect()" [disabled]="busy()">{{ 'googleCalendar.disconnect' | transloco }}</button>
          } @else {
            <button mat-flat-button (click)="connect()" [disabled]="busy()">{{ 'googleCalendar.connect' | transloco }}</button>
          }
        </section>
      }
    }
  `,
  styles: `
    .gcal {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      margin: 0 0 var(--space-5);
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    .gcal.on { background: color-mix(in srgb, var(--mat-sys-primary) 5%, transparent); }
    .lead { color: #1a73e8; flex: none; }
    .text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; overflow-wrap: anywhere; }
    .muted { font: var(--mat-sys-body-small); }
    button { flex: none; }
    @media (max-width: 599px) {
      .gcal { flex-wrap: wrap; }
      button { margin-left: auto; }
    }
  `,
})
export class GoogleCalendar {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);

  /** Changes when the business setting changes, so the card appears or disappears at once. */
  readonly businessSync = input<boolean | null>(null);

  protected readonly status = signal<Schemas['GoogleCalendarStatusDto'] | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    effect(() => {
      this.businessSync();
      untracked(() => void this.load());
    });
  }

  protected async connect(): Promise<void> {
    this.busy.set(true);
    const { data, error } = await this.api.POST('/api/calendar/google/connect');
    if (error || !data) {
      this.busy.set(false);
      if (error) this.notifier.error(error);
      return;
    }
    // Google asks for consent and comes back to /admin?google=...
    globalThis.location.assign(data.url);
  }

  protected async disconnect(): Promise<void> {
    this.busy.set(true);
    const { error } = await this.api.DELETE('/api/calendar/google/connection');
    this.busy.set(false);
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/calendar/google/status');
    this.status.set(data ?? null);
  }
}
