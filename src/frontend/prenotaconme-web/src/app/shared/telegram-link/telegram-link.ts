import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';

/**
 * F3-T2: link this account to the business's Telegram bot (deep link t.me/<bot>?start=<code>). Hidden when the
 * business has no bot. The state is read again when the user comes back from Telegram.
 */
@Component({
  selector: 'app-telegram-link',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    @if (bot()) {
      <section class="telegram" [class.on]="linked()">
        <mat-icon class="lead" aria-hidden="true">send</mat-icon>
        <div class="text">
          <strong>Telegram</strong>
          <span class="muted">{{ (linked() ? 'telegram.linked' : 'telegram.linkHint') | transloco: { bot: bot() } }}</span>
        </div>
        @if (linked()) {
          <button mat-button (click)="unlink()" [disabled]="busy()">{{ 'telegram.unlink' | transloco }}</button>
        } @else {
          <button mat-flat-button (click)="link()" [disabled]="busy()">{{ 'telegram.link' | transloco }}</button>
        }
      </section>
    }
  `,
  styles: `
    .telegram {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      margin: 0 0 var(--space-5);
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    .telegram.on { background: color-mix(in srgb, var(--mat-sys-primary) 5%, transparent); }
    .lead { color: #229ed9; flex: none; }
    .text { flex: 1; display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .muted { font: var(--mat-sys-body-small); }
    button { flex: none; }
    @media (max-width: 599px) {
      .telegram { flex-wrap: wrap; }
      button { margin-left: auto; }
    }
  `,
})
export class TelegramLink {
  private readonly api = inject(API_CLIENT);

  /** Business slug for public pages; in the backoffice the selected business is used. */
  readonly slug = input<string | null>(null);

  protected readonly bot = signal<string | null>(null);
  protected readonly linked = signal(false);
  protected readonly busy = signal(false);

  constructor() {
    effect(() => {
      this.slug();
      untracked(() => void this.load());
    });

    const onFocus = () => void this.load();
    window.addEventListener('focus', onFocus);
    inject(DestroyRef).onDestroy(() => window.removeEventListener('focus', onFocus));
  }

  /** The tab is opened before the await so popup blockers allow it. */
  protected async link(): Promise<void> {
    const tab = window.open('', '_blank');
    this.busy.set(true);
    const { data } = await this.api.POST('/api/notifications/telegram/link', { headers: this.headers() });
    this.busy.set(false);
    if (!tab) return;
    if (data) {
      tab.opener = null;
      tab.location.href = data.url;
    } else {
      tab.close();
    }
  }

  protected async unlink(): Promise<void> {
    this.busy.set(true);
    await this.api.DELETE('/api/notifications/telegram/link', { headers: this.headers() });
    this.busy.set(false);
    this.linked.set(false);
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/notifications/telegram/link', { headers: this.headers() });
    this.bot.set(data?.botUsername ?? null);
    this.linked.set(data?.linked ?? false);
  }

  private headers(): Record<string, string> | undefined {
    const slug = this.slug();
    return slug ? { 'X-Tenant': slug } : undefined;
  }
}
