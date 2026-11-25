import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';
import { Notifier } from '../../shared/api-errors';
import { confirmDelete } from '../../shared/confirm-dialog';

/** F3-T1: connect the business's Telegram bot (token from @BotFather). Admin only, on the dashboard. */
@Component({
  selector: 'app-telegram-settings',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, TranslocoPipe],
  template: `
    @if (loaded()) {
      <section class="telegram">
        <h2>{{ 'telegram.botTitle' | transloco }}</h2>
        @if (username(); as name) {
          <p class="muted">{{ 'telegram.botConnected' | transloco }}</p>
          <div class="connected">
            <a mat-stroked-button [href]="'https://t.me/' + name" target="_blank" rel="noopener">
              <mat-icon>send</mat-icon>&#64;{{ name }}
            </a>
            <button mat-button (click)="remove()" [disabled]="busy()">{{ 'telegram.remove' | transloco }}</button>
          </div>
        } @else {
          <p class="muted">{{ 'telegram.botHint' | transloco }}</p>
          <ol class="muted steps">
            <li>{{ 'telegram.step1' | transloco }}</li>
            <li>{{ 'telegram.step2' | transloco }}</li>
            <li>{{ 'telegram.step3' | transloco }}</li>
          </ol>
          <form class="token" (ngSubmit)="save()">
            <mat-form-field subscriptSizing="dynamic">
              <mat-label>{{ 'telegram.token' | transloco }}</mat-label>
              <input matInput name="token" [(ngModel)]="token" autocomplete="off" spellcheck="false" placeholder="123456789:AA…" />
            </mat-form-field>
            <button mat-flat-button type="submit" [disabled]="!token.trim() || busy()">{{ 'telegram.connect' | transloco }}</button>
          </form>
        }
      </section>
    }
  `,
  styles: `
    .telegram {
      margin: 0 0 var(--space-6);
      padding: var(--space-5);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    h2 { margin: 0 0 var(--space-1); font: var(--mat-sys-title-medium); font-weight: 600; }
    .steps { margin: var(--space-2) 0 var(--space-4); padding-left: var(--space-5); font: var(--mat-sys-body-small); }
    .connected, .token { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); margin-top: var(--space-3); }
    .token mat-form-field { flex: 1; min-width: 240px; }
  `,
})
export class TelegramSettings implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly username = signal<string | null>(null);
  protected token = '';

  ngOnInit(): void {
    void this.load();
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    const { data, error } = await this.api.PUT('/api/notifications/telegram/bot', { body: { token: this.token.trim() } });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.token = '';
    this.username.set(data.username ?? null);
    this.notifier.saved();
  }

  protected async remove(): Promise<void> {
    if (!(await confirmDelete(this.dialog, this.transloco.translate('telegram.confirmRemove')))) return;
    this.busy.set(true);
    const { error } = await this.api.DELETE('/api/notifications/telegram/bot');
    this.busy.set(false);
    if (error) this.notifier.error(error);
    else this.username.set(null);
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/notifications/telegram/bot');
    this.username.set(data?.username ?? null);
    this.loaded.set(true);
  }
}
