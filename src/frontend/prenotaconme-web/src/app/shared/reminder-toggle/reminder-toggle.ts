import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';
import { Notifier } from '../api-errors';

/** F2-06: the client turns appointment reminders from this business on or off (on by default). */
@Component({
  selector: 'app-reminder-toggle',
  imports: [MatIconModule, MatSlideToggleModule, TranslocoPipe],
  template: `
    @if (enabled() !== null) {
      <section class="reminders" [class.on]="enabled()">
        <mat-icon class="lead" aria-hidden="true">alarm</mat-icon>
        <div class="text">
          <strong id="reminders-title">{{ 'reminders.title' | transloco }}</strong>
          <span class="muted">{{ (enabled() ? 'reminders.on' : 'reminders.off') | transloco }}</span>
        </div>
        <mat-slide-toggle aria-labelledby="reminders-title" [checked]="enabled()" [disabled]="busy()" (change)="save($event.checked)" />
      </section>
    }
  `,
  styles: `
    .reminders {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      margin: 0 0 var(--space-5);
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    .reminders.on { background: color-mix(in srgb, var(--mat-sys-primary) 5%, transparent); }
    .lead { color: var(--mat-sys-primary); flex: none; }
    .text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .muted { font: var(--mat-sys-body-small); }
  `,
})
export class ReminderToggle {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);

  /** Business slug of the public page. */
  readonly slug = input.required<string>();

  protected readonly enabled = signal<boolean | null>(null);
  protected readonly busy = signal(false);

  constructor() {
    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug));
    });
  }

  protected async save(enabled: boolean): Promise<void> {
    this.busy.set(true);
    const { data, error } = await this.api.PUT('/api/notifications/reminders/preference', {
      headers: { 'X-Tenant': this.slug() },
      body: { enabled },
    });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      this.enabled.set(!enabled);
      return;
    }
    this.enabled.set(data.enabled);
  }

  private async load(slug: string): Promise<void> {
    const { data } = await this.api.GET('/api/notifications/reminders/preference', { headers: { 'X-Tenant': slug } });
    this.enabled.set(data?.enabled ?? null);
  }
}
