import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';

export interface AppointmentDialogData {
  appointment: Schemas['CalendarAppointmentDto'];
  staff: Schemas['CalendarStaffDto'][];
  locations: Schemas['LocationDto'][];
  canEdit: boolean;
}

/**
 * Details of one appointment; Admins can hand it to another person or cancel it. Once it has started, the business
 * records how it went (F4-01): completed with the amount charged, or no-show. Closes with true on change.
 */
@Component({
  selector: 'app-appointment-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ a.serviceName }}</h2>
    <mat-dialog-content>
      <dl>
        <div>
          <dt><mat-icon>event</mat-icon></dt>
          <dd>{{ when() }}</dd>
        </div>
        <div>
          <dt><mat-icon>person</mat-icon></dt>
          <dd>
            <strong>{{ a.clientName || ('calendar.noName' | transloco) }}</strong>
            @if (a.isGuest) {
              <span class="pill">{{ 'calendar.guest' | transloco }}</span>
            }
            @if (a.clientPhone) {
              <br /><a [href]="'tel:' + a.clientPhone">{{ a.clientPhone }}</a>
            }
            @if (a.clientEmail) {
              <br /><a [href]="'mailto:' + a.clientEmail">{{ a.clientEmail }}</a>
            }
            @if (data.canEdit) {
              <br /><button mat-button class="card-link" (click)="openClient()">
                <mat-icon>contact_page</mat-icon>{{ 'clients.open' | transloco }}
              </button>
            }
          </dd>
        </div>
        @if (locationName()) {
          <div>
            <dt><mat-icon>location_on</mat-icon></dt>
            <dd>{{ locationName() }}</dd>
          </div>
        }
        <div>
          <dt><mat-icon>euro</mat-icon></dt>
          <dd>{{ price() }}</dd>
        </div>
      </dl>

      @if (started()) {
        <section class="outcome">
          <h3>{{ 'calendar.outcome.title' | transloco }}</h3>
          <p class="muted hint">{{ 'calendar.outcome.status.' + a.status | transloco }}</p>
          <mat-form-field appearance="outline" class="amount" subscriptSizing="dynamic">
            <mat-label>{{ 'calendar.outcome.charged' | transloco }}</mat-label>
            <input matInput type="number" min="0" step="0.5" [value]="charged()" (input)="charged.set(+$any($event.target).value)" />
            <span matTextSuffix>€</span>
          </mat-form-field>
          <div class="outcome-actions">
            <button mat-stroked-button [class.selected]="a.status === 'Completed'" (click)="outcome('Completed')" [disabled]="busy()">
              <mat-icon>check_circle</mat-icon>{{ 'calendar.outcome.completed' | transloco }}
            </button>
            <button mat-stroked-button [class.selected]="a.status === 'NoShow'" (click)="outcome('NoShow')" [disabled]="busy()">
              <mat-icon>person_off</mat-icon>{{ 'calendar.outcome.noShow' | transloco }}
            </button>
            @if (a.status !== 'Confirmed') {
              <button mat-button (click)="outcome('Confirmed')" [disabled]="busy()">{{ 'calendar.outcome.undo' | transloco }}</button>
            }
          </div>
        </section>
      }

      @if (data.canEdit && a.status === 'Confirmed') {
        <mat-form-field appearance="outline">
          <mat-label>{{ 'calendar.staff' | transloco }}</mat-label>
          <mat-select [value]="staffId()" (valueChange)="staffId.set($event)">
            @for (s of eligibleStaff(); track s.id) {
              <mat-option [value]="s.id">{{ s.displayName }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <p class="muted hint">{{ 'calendar.dragHint' | transloco }}</p>
      } @else {
        <p>{{ 'calendar.with' | transloco }} <strong>{{ staffName() }}</strong></p>
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      @if (data.canEdit && a.status === 'Confirmed') {
        <button mat-button class="danger" (click)="cancel()" [disabled]="busy()">
          {{ (confirming() ? 'calendar.confirmCancel' : 'calendar.cancelAppointment') | transloco }}
        </button>
      }
      <span class="spacer"></span>
      <button mat-button mat-dialog-close>{{ 'calendar.close' | transloco }}</button>
      @if (data.canEdit && a.status === 'Confirmed') {
        <button mat-flat-button (click)="save()" [disabled]="busy() || staffId() === a.staffMemberId">
          {{ 'common.save' | transloco }}
        </button>
      }
    </mat-dialog-actions>
  `,
  styles: `
    dl { display: grid; gap: var(--space-3); margin: 0 0 var(--space-4); }
    dl > div { display: flex; gap: var(--space-3); align-items: flex-start; }
    dt { color: var(--mat-sys-on-surface-variant); }
    dd { margin: 0; }
    dd::first-letter { text-transform: uppercase; }
    .pill { margin-left: var(--space-2); }
    mat-form-field { width: 100%; }
    .hint { margin: 0; font: var(--mat-sys-body-small); }
    .danger { color: var(--mat-sys-error); }
    .card-link { margin-left: calc(-1 * var(--space-3)); }
    .spacer { flex: 1; }
    .outcome { margin: 0 0 var(--space-4); padding: var(--space-3) var(--space-4); border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--radius-l); }
    .outcome h3 { margin: 0; font: var(--mat-sys-title-small); font-weight: 600; }
    .outcome .hint { margin: 2px 0 var(--space-3); }
    .amount { width: 160px; }
    .outcome-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); margin-top: var(--space-3); }
    .outcome-actions .selected { background: var(--mat-sys-secondary-container); }
  `,
})
export class AppointmentDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);
  private readonly ref = inject(MatDialogRef<AppointmentDialog, boolean>);
  private readonly router = inject(Router);
  protected readonly data = inject<AppointmentDialogData>(MAT_DIALOG_DATA);
  protected readonly a = this.data.appointment;

  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  protected readonly staffId = signal(this.a.staffMemberId);
  protected readonly busy = signal(false);
  protected readonly confirming = signal(false);

  /** People who work at this location and perform this service (plus the current one). */
  protected readonly eligibleStaff = computed(() =>
    this.data.staff.filter(
      (s) => s.id === this.a.staffMemberId || (s.isActive && s.locationIds.includes(this.a.locationId) && s.serviceIds.includes(this.a.serviceId)),
    ),
  );

  protected readonly staffName = computed(() => this.data.staff.find((s) => s.id === this.a.staffMemberId)?.displayName ?? '');
  protected readonly locationName = computed(() =>
    this.data.locations.length > 1 ? (this.data.locations.find((l) => l.id === this.a.locationId)?.name ?? '') : '',
  );

  protected readonly when = computed(() => {
    const day = new Intl.DateTimeFormat(this.lang(), { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(this.a.startsAt));
    const time = (iso: string) => new Intl.DateTimeFormat(this.lang(), { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(new Date(iso));
    return `${day}, ${time(this.a.startsAt)}–${time(this.a.endsAt)}`;
  });

  protected readonly price = computed(() => {
    const money = (v: number) => new Intl.NumberFormat(this.lang(), { style: 'currency', currency: 'EUR' }).format(v);
    return this.a.chargedAmount != null ? `${money(this.a.chargedAmount)} (${money(this.a.price)})` : money(this.a.price);
  });

  /** F4-01: the outcome can be recorded once the appointment has started. */
  protected readonly started = computed(() => this.a.status !== 'Cancelled' && new Date(this.a.startsAt).getTime() <= Date.now());
  protected readonly charged = signal(this.a.chargedAmount ?? this.a.price);

  protected async outcome(outcome: 'Completed' | 'NoShow' | 'Confirmed'): Promise<void> {
    this.busy.set(true);
    const { error } = await this.api.POST('/api/scheduling/calendar/appointments/{id}/outcome', {
      params: { path: { id: this.a.id } },
      body: { outcome, chargedAmount: outcome === 'Completed' ? this.charged() : null },
    });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.notifier.saved();
    this.ref.close(true);
  }

  protected openClient(): void {
    this.ref.close(false);
    void this.router.navigate(['/admin/clienti/scheda'], { queryParams: { key: this.a.clientKey } });
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    const { error } = await this.api.PUT('/api/scheduling/calendar/appointments/{id}', {
      params: { path: { id: this.a.id } },
      body: { staffId: this.staffId(), startsAt: this.a.startsAt },
    });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.notifier.saved();
    this.ref.close(true);
  }

  protected async cancel(): Promise<void> {
    if (!this.confirming()) {
      this.confirming.set(true);
      return;
    }
    this.busy.set(true);
    const { error } = await this.api.POST('/api/scheduling/calendar/appointments/{id}/cancel', {
      params: { path: { id: this.a.id } },
    });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.notifier.saved();
    this.ref.close(true);
  }
}
