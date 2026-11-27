import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';
import { AuthService } from '../../core/auth/auth.service';
import { Notifier } from '../api-errors';
import { confirmAction } from '../confirm-dialog';

/** F5-04 (GDPR): the signed-in person downloads everything we keep about them, or deletes their account. */
@Component({
  selector: 'app-my-data',
  imports: [MatButtonModule, MatIconModule, RouterLink, TranslocoPipe],
  template: `
    <section class="my-data">
      <h2>{{ 'myData.title' | transloco }}</h2>
      <p class="muted">
        {{ 'myData.hint' | transloco }}
        <a routerLink="/privacy">{{ 'privacy.link' | transloco }}</a>
      </p>
      <div class="actions">
        <button mat-stroked-button (click)="download()" [disabled]="busy()">
          <mat-icon>download</mat-icon>{{ 'myData.download' | transloco }}
        </button>
        <button mat-button class="danger" (click)="deleteAccount()" [disabled]="busy()">
          <mat-icon>delete_forever</mat-icon>{{ 'myData.delete' | transloco }}
        </button>
      </div>
    </section>
  `,
  styles: `
    .my-data { margin-top: var(--space-6); padding-top: var(--space-4); border-top: 1px solid var(--mat-sys-outline-variant); }
    h2 { font: var(--mat-sys-title-small); font-weight: 600; margin: 0 0 var(--space-1); }
    .muted { margin: 0 0 var(--space-3); font: var(--mat-sys-body-small); }
    .actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }
    .danger { color: var(--mat-sys-error); }
  `,
})
export class MyData {
  private readonly api = inject(API_CLIENT);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly busy = signal(false);

  protected async download(): Promise<void> {
    this.busy.set(true);
    const { data, error } = await this.api.GET('/api/users/me/export', { parseAs: 'blob' });
    this.busy.set(false);
    if (error || !data) {
      this.notifier.error(error);
      return;
    }
    const url = URL.createObjectURL(data as Blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'prenotaconme-data.json';
    link.click();
    URL.revokeObjectURL(url);
  }

  protected async deleteAccount(): Promise<void> {
    const ok = await confirmAction(this.dialog, this.transloco.translate('myData.confirm'), this.transloco.translate('myData.delete'));
    if (!ok) return;

    this.busy.set(true);
    const { error } = await this.api.DELETE('/api/users/me');
    if (error) {
      this.busy.set(false);
      this.notifier.error(error);
      return;
    }
    try {
      await this.auth.deleteAccount();
    } catch {
      // The data is already gone; the Firebase login will be recreated empty if used again.
    }
    await this.auth.signOut();
    this.busy.set(false);
    this.notifier.saved();
  }
}
