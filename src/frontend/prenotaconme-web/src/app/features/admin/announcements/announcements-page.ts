import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { confirmAction, confirmDelete } from '../../../shared/confirm-dialog';
import { todayIso } from '../../booking/booking-format';
import { AnnouncementDialog, AnnouncementDialogData } from './announcement-dialog';
import { announcementStatus, formatPeriod } from './announcement-status';

type Announcement = Schemas['AnnouncementDto'];

/**
 * F2-03: messages from the business to its clients (closures, holiday hours, informative surcharges). They show
 * on the booking page while valid and can be sent once to every client by push and e-mail.
 */
@Component({
  selector: 'app-announcements-page',
  imports: [DatePipe, MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.announcements' | transloco }}</h1>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'announcements.new' | transloco }}</button>
    </header>
    <p class="muted intro">{{ 'announcements.intro' | transloco }}</p>

    @for (a of announcements(); track a.id) {
      @let status = statusOf(a);
      <article class="card" [class.ended]="status === 'ended'">
        <div class="head">
          <h2>{{ a.title }}</h2>
          <span class="pill" [class.ok]="status === 'active'">{{ 'announcements.status.' + status | transloco }}</span>
        </div>
        <p class="period muted"><mat-icon inline>event</mat-icon>{{ period(a) }}</p>
        <p class="message">{{ a.message }}</p>
        <div class="foot">
          @if (a.sentAt) {
            <span class="muted sent">
              <mat-icon inline>done_all</mat-icon>
              {{ 'announcements.sent' | transloco: { count: a.recipientCount ?? 0, date: (a.sentAt | date: 'd MMM, HH:mm' : undefined : lang()) } }}
            </span>
          } @else if (status !== 'ended') {
            <button mat-stroked-button (click)="send(a)" [disabled]="busy()">
              <mat-icon>send</mat-icon>{{ 'announcements.send' | transloco }}
            </button>
          }
          <span class="spacer"></span>
          <button mat-icon-button (click)="edit(a)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button (click)="remove(a)" [attr.aria-label]="'common.delete' | transloco"><mat-icon>delete</mat-icon></button>
        </div>
      </article>
    } @empty {
      @if (loaded()) {
        <div class="empty">
          <mat-icon>campaign</mat-icon>
          <p class="muted">{{ 'announcements.empty' | transloco }}</p>
        </div>
      }
    }
  `,
  styles: `
    .intro { margin: calc(-1 * var(--space-3)) 0 var(--space-5); max-width: 640px; }
    .card {
      padding: var(--space-4) var(--space-5);
      margin-bottom: var(--space-3);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
      background: var(--mat-sys-surface);
    }
    .card.ended { opacity: 0.65; }
    .head { display: flex; align-items: center; gap: var(--space-3); }
    h2 { margin: 0; font: var(--mat-sys-title-medium); font-weight: 600; flex: 1; }
    .period { display: flex; align-items: center; gap: var(--space-1); margin: var(--space-1) 0 var(--space-2); font: var(--mat-sys-body-small); }
    .message { white-space: pre-line; margin: 0 0 var(--space-3); }
    .foot { display: flex; align-items: center; gap: var(--space-2); }
    .sent { display: inline-flex; align-items: center; gap: var(--space-1); font: var(--mat-sys-body-small); }
    .spacer { flex: 1; }
    .empty { text-align: center; padding: var(--space-7) 0; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; color: var(--mat-sys-on-surface-variant); }
  `,
})
export class AnnouncementsPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  protected readonly announcements = signal<Announcement[]>([]);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  private readonly audience = signal<Schemas['AnnouncementAudienceDto'] | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  protected statusOf = (a: Announcement) => announcementStatus(a.startsOn, a.endsOn, todayIso());
  protected period = (a: Announcement) => formatPeriod(a.startsOn, a.endsOn, this.lang());

  protected async edit(announcement: Announcement | null): Promise<void> {
    const data: AnnouncementDialogData = { announcement, audience: this.audience() };
    const saved = await firstValueFrom(this.dialog.open(AnnouncementDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  protected async send(a: Announcement): Promise<void> {
    const message = this.transloco.translate('announcements.confirmSend', { count: this.audience()?.clients ?? 0 });
    if (!(await confirmAction(this.dialog, message, this.transloco.translate('announcements.send')))) return;
    this.busy.set(true);
    const { error } = await this.api.POST('/api/notifications/announcements/{id}/send', { params: { path: { id: a.id } } });
    this.busy.set(false);
    if (error) this.notifier.error(error);
    await this.load();
  }

  protected async remove(a: Announcement): Promise<void> {
    if (!(await confirmDelete(this.dialog, this.transloco.translate('announcements.confirmDelete', { title: a.title })))) return;
    const { error } = await this.api.DELETE('/api/notifications/announcements/{id}', { params: { path: { id: a.id } } });
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const [list, audience] = await Promise.all([
      this.api.GET('/api/notifications/announcements'),
      this.api.GET('/api/notifications/announcements/audience'),
    ]);
    this.announcements.set(list.data ?? []);
    this.audience.set(audience.data ?? null);
    this.loaded.set(true);
  }
}
