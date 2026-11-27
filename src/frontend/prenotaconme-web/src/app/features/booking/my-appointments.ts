import { InAppBrowserNotice } from '../../shared/in-app-browser/in-app-browser-notice';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AnnouncementBanner } from './announcement-banner';
import { API_CLIENT, assetUrl, Schemas } from '../../core/api/api-client';
import { MyData } from '../../shared/my-data/my-data';
import { AuthService, signInError, SignInProvider } from '../../core/auth/auth.service';
import { firstErrorCode } from '../../shared/api-errors';
import { PushToggle } from '../../shared/push-toggle/push-toggle';
import { ReminderToggle } from '../../shared/reminder-toggle/reminder-toggle';
import { TelegramLink } from '../../shared/telegram-link/telegram-link';
import { addDaysIso, dayParts, formatDay, formatInstant, formatPrice, groupByPeriod, initials, todayIso } from './booking-format';

type Appointment = Schemas['MyAppointmentDto'];
type Day = Schemas['AvailabilityDayDto'];
type Slot = Schemas['SlotDto'];

const DAYS_PER_PAGE = 14;

/**
 * F1-12: /:slug/prenotazioni. The signed-in client's appointments with this business: upcoming ones can be
 * cancelled or moved to another free time (same service, location and person) until the business notice deadline.
 */
@Component({
  selector: 'app-my-appointments',
  imports: [AnnouncementBanner, InAppBrowserNotice, RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule, PushToggle, ReminderToggle, TelegramLink, MyData, TranslocoPipe],
  templateUrl: './my-appointments.html',
  styleUrls: ['./booking-page.scss', './business-header.scss', './my-appointments.scss'],
})
export class MyAppointments {
  private readonly api = inject(API_CLIENT);
  private readonly auth = inject(AuthService);
  private readonly transloco = inject(TranslocoService);

  /** Route param :slug */
  readonly slug = input.required<string>();

  protected readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  protected readonly authReady = this.auth.ready;
  protected readonly signedIn = this.auth.isSignedIn;

  protected readonly state = signal<'loading' | 'ready' | 'notFound'>('loading');
  protected readonly tenant = signal<Schemas['PublicTenantDto'] | null>(null);
  /** F5-08: business images (absolute URLs), null without one. */
  protected readonly logoUrl = computed(() => assetUrl(this.tenant()?.logoUrl));
  protected readonly coverUrl = computed(() => assetUrl(this.tenant()?.coverUrl));
  protected readonly data = signal<Schemas['MyAppointmentsDto'] | null>(null);
  /** Translation key of the last error. */
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** Appointment waiting for the second click on "cancel". */
  protected readonly confirmingCancel = signal<string | null>(null);

  // Reschedule panel
  protected readonly moving = signal<Appointment | null>(null);
  protected readonly from = signal(todayIso());
  protected readonly days = signal<Day[]>([]);
  protected readonly loadingSlots = signal(false);
  protected readonly selectedDay = signal<string | null>(null);
  protected readonly periods = computed(() =>
    groupByPeriod(this.days().find((d) => d.date === this.selectedDay())?.slots ?? []),
  );

  protected readonly upcoming = computed(() =>
    (this.data()?.appointments ?? [])
      .filter((a) => a.status === 'Confirmed' && new Date(a.endsAt).getTime() > Date.now())
      .sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
  );

  protected readonly past = computed(() => {
    const upcoming = new Set(this.upcoming().map((a) => a.id));
    return (this.data()?.appointments ?? []).filter((a) => !upcoming.has(a.id));
  });

  constructor() {
    effect(() => {
      const slug = this.slug();
      const ready = this.auth.ready();
      const signedIn = this.auth.isSignedIn();
      untracked(() => {
        if (ready) void this.load(slug, signedIn);
      });
    });
  }

  // ---- formatting -------------------------------------------------------------------------------------------
  protected when = (a: Appointment) => formatInstant(a.startsAt, a.timeZone, this.lang());
  protected deadline = (a: Appointment) => formatInstant(a.changeableUntil, a.timeZone, this.lang());
  protected price = (value: number) => formatPrice(value, this.lang());
  protected day = (iso: string) => formatDay(iso, this.lang());
  protected chip = (iso: string) => dayParts(iso, this.lang());
  protected initials = initials;
  protected place = (a: Appointment) => [a.locationAddress, a.locationCity].filter((x) => !!x).join(', ');

  // ---- actions ----------------------------------------------------------------------------------------------
  protected async signIn(provider: SignInProvider): Promise<void> {
    this.error.set(null);
    try {
      await this.auth.signIn(provider);
    } catch (e) {
      this.error.set(signInError(e));
    }
  }

  protected async cancel(a: Appointment): Promise<void> {
    if (this.confirmingCancel() !== a.id) {
      this.confirmingCancel.set(a.id);
      return;
    }

    this.busy.set(true);
    this.clearMessages();
    const { error } = await this.api.POST('/api/scheduling/appointments/{id}/cancel', {
      headers: this.tenantHeader(),
      params: { path: { id: a.id } },
    });
    this.busy.set(false);
    this.confirmingCancel.set(null);

    if (error) {
      this.error.set(`errors.${firstErrorCode(error)}`);
    } else {
      this.message.set('myAppointments.cancelled');
    }
    await this.reload();
  }

  protected async startMove(a: Appointment): Promise<void> {
    this.clearMessages();
    this.confirmingCancel.set(null);
    this.moving.set(a);
    this.from.set(todayIso());
    this.selectedDay.set(null);
    await this.loadSlots(false);
  }

  protected stopMove(): void {
    this.moving.set(null);
    this.days.set([]);
  }

  protected async moreDays(): Promise<void> {
    this.from.set(addDaysIso(this.from(), DAYS_PER_PAGE));
    await this.loadSlots(true);
  }

  protected isCurrent(slot: Slot): boolean {
    return new Date(slot.startsAt).getTime() === new Date(this.moving()?.startsAt ?? 0).getTime();
  }

  protected async moveTo(slot: Slot): Promise<void> {
    const a = this.moving();
    if (!a) return;

    this.busy.set(true);
    this.clearMessages();
    const { error } = await this.api.POST('/api/scheduling/appointments/{id}/reschedule', {
      headers: this.tenantHeader(),
      params: { path: { id: a.id } },
      body: { startsAt: slot.startsAt, staffId: a.staffMemberId },
    });
    this.busy.set(false);

    if (error) {
      // Typically the time was just taken: offer fresh times.
      this.error.set(`errors.${firstErrorCode(error)}`);
      await this.loadSlots(false);
      return;
    }

    this.stopMove();
    this.message.set('myAppointments.moved');
    await this.reload();
  }

  // ---- data -------------------------------------------------------------------------------------------------
  private tenantHeader() {
    return { 'X-Tenant': this.slug() };
  }

  private clearMessages(): void {
    this.error.set(null);
    this.message.set(null);
  }

  private async load(slug: string, signedIn: boolean): Promise<void> {
    this.state.set('loading');
    this.data.set(null);
    const tenant = await this.api.GET('/api/tenancy/tenants/{slug}', {
      params: { path: { slug } },
      headers: { 'X-Tenant': '' },
    });
    if (!tenant.data) {
      this.state.set('notFound');
      return;
    }

    this.tenant.set(tenant.data);
    if (signedIn) await this.reload();
    this.state.set('ready');
  }

  private async reload(): Promise<void> {
    const { data } = await this.api.GET('/api/scheduling/appointments/mine', { headers: this.tenantHeader() });
    this.data.set(data ?? null);
  }

  private async loadSlots(append: boolean): Promise<void> {
    const a = this.moving();
    if (!a) return;

    this.loadingSlots.set(true);
    const { data } = await this.api.GET('/api/scheduling/availability', {
      headers: this.tenantHeader(),
      params: {
        query: {
          locationId: a.locationId,
          serviceId: a.serviceId,
          staffId: a.staffMemberId,
          from: this.from(),
          days: DAYS_PER_PAGE,
          rescheduling: a.id,
        },
      },
    });
    this.loadingSlots.set(false);

    const days = data?.days ?? [];
    this.days.set(append ? [...this.days(), ...days] : days);
    const current = this.selectedDay();
    if (!current || !this.days().some((d) => d.date === current && d.slots.length > 0)) {
      this.selectedDay.set(this.days().find((d) => d.slots.length > 0)?.date ?? null);
    }
  }
}
