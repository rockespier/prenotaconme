import { Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AnnouncementBanner } from './announcement-banner';
import { API_CLIENT, Schemas } from '../../core/api/api-client';
import { AuthService } from '../../core/auth/auth.service';
import { SessionService } from '../../core/session/session.service';
import { firstErrorCode } from '../../shared/api-errors';
import { addDaysIso, dayParts, formatDay, formatDuration, formatPrice, groupByPeriod, initials, todayIso } from './booking-format';

type Step = 'location' | 'service' | 'staff' | 'time' | 'confirm' | 'done';
type Slot = Schemas['SlotDto'];
type Day = Schemas['AvailabilityDayDto'];

const DAYS_PER_PAGE = 14;

/**
 * F1-10: public booking page at /:slug. Location → service → staff (or "no preference") → day and time →
 * sign in with Google (+ phone the first time) → confirmation. Every API call sends this business as X-Tenant.
 */
@Component({
  selector: 'app-booking-page',
  imports: [AnnouncementBanner, 
    ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatProgressSpinnerModule, TranslocoPipe,
  ],
  templateUrl: './booking-page.html',
  styleUrl: './booking-page.scss',
})
export class BookingPage {
  private readonly api = inject(API_CLIENT);
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionService);
  private readonly transloco = inject(TranslocoService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  /** Route param :slug */
  readonly slug = input.required<string>();

  protected readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  protected readonly signedIn = this.auth.isSignedIn;

  protected readonly state = signal<'loading' | 'ready' | 'notFound'>('loading');
  protected readonly tenant = signal<Schemas['PublicTenantDto'] | null>(null);
  protected readonly options = signal<Schemas['BookingOptionsDto'] | null>(null);
  protected readonly step = signal<Step>('location');

  protected readonly location = signal<Schemas['PublicLocationDto'] | null>(null);
  protected readonly service = signal<Schemas['PublicServiceDto'] | null>(null);
  /** null = no preference ("any staff"). */
  protected readonly staff = signal<Schemas['PublicStaffDto'] | null>(null);

  protected readonly from = signal(todayIso());
  protected readonly days = signal<Day[]>([]);
  protected readonly loadingSlots = signal(false);
  protected readonly selectedDay = signal<string | null>(null);
  protected readonly slot = signal<Slot | null>(null);

  protected readonly busy = signal(false);
  /** Translation key of the last error shown to the visitor. */
  protected readonly error = signal<string | null>(null);
  protected readonly needsPhone = signal(false);
  protected readonly appointment = signal<Schemas['AppointmentDto'] | null>(null);
  protected readonly phone = new FormControl('+39 ', {
    nonNullable: true,
    validators: [Validators.required, Validators.pattern(/^\+[0-9 .-]{8,20}$/)],
  });

  protected readonly servicesHere = computed(() =>
    (this.options()?.services ?? []).filter((s) => s.locationIds.includes(this.location()?.id ?? '')),
  );

  protected readonly staffForService = computed(() =>
    (this.options()?.staff ?? []).filter(
      (p) => p.locationIds.includes(this.location()?.id ?? '') && p.serviceIds.includes(this.service()?.id ?? ''),
    ),
  );

  protected readonly slotsOfDay = computed(() => this.days().find((d) => d.date === this.selectedDay())?.slots ?? []);

  /** Free times of the selected day split into morning / afternoon / evening. */
  protected readonly periods = computed(() => groupByPeriod(this.slotsOfDay()));

  /** Steps the visitor actually goes through (single location or single staff member are skipped). */
  protected readonly steps = computed<Step[]>(() => {
    const steps: Step[] = [];
    if ((this.options()?.locations.length ?? 0) > 1) steps.push('location');
    steps.push('service');
    const staffHere = (this.options()?.staff ?? []).filter((p) => p.locationIds.includes(this.location()?.id ?? ''));
    if (this.service() ? this.staffForService().length > 1 : staffHere.length > 1) steps.push('staff');
    steps.push('time', 'confirm');
    return steps;
  });

  /** 1-based position of the current step, for "Passo 2 di 4". */
  protected readonly stepNumber = computed(() => this.steps().indexOf(this.step()) + 1);

  /** Who will do the service: the chosen person, or the one assigned by the server for "no preference". */
  protected readonly staffName = computed(() => {
    const chosen = this.staff();
    if (chosen) return chosen.displayName;
    const assignedId = this.appointment()?.staffMemberId;
    return (this.options()?.staff ?? []).find((p) => p.id === assignedId)?.displayName ?? null;
  });

  constructor() {
    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug));
    });
  }

  // ---- formatting -------------------------------------------------------------------------------------------
  protected price = (value: number) => formatPrice(value, this.lang());
  protected day = (iso: string, style: 'long' | 'short' = 'long') => formatDay(iso, this.lang(), style);
  protected duration = formatDuration;
  protected chip = (iso: string) => dayParts(iso, this.lang());
  protected initials = initials;
  /** "Via Roma 1, Milano" (either part may be missing). */
  protected place = (l: Schemas['PublicLocationDto'] | null) => [l?.address, l?.city].filter((x) => !!x).join(', ');

  // ---- steps ------------------------------------------------------------------------------------------------
  protected chooseLocation(location: Schemas['PublicLocationDto']): void {
    this.location.set(location);
    this.service.set(null);
    this.step.set('service');
  }

  protected chooseService(service: Schemas['PublicServiceDto']): void {
    this.service.set(service);
    this.staff.set(null);
    // Skip the staff step when there is nobody to choose between.
    if (this.staffForService().length <= 1) {
      void this.goToTime();
    } else {
      this.step.set('staff');
    }
  }

  protected chooseStaff(staff: Schemas['PublicStaffDto'] | null): void {
    this.staff.set(staff);
    void this.goToTime();
  }

  protected chooseSlot(slot: Slot): void {
    this.slot.set(slot);
    this.error.set(null);
    this.step.set('confirm');
    void this.checkPhone();
  }

  protected back(): void {
    const order: Step[] = ['location', 'service', 'staff', 'time', 'confirm'];
    let i = order.indexOf(this.step()) - 1;
    if (order[i] === 'staff' && this.staffForService().length <= 1) i--;
    if (order[i] === 'location' && (this.options()?.locations.length ?? 0) <= 1) return;
    this.error.set(null);
    if (i >= 0) this.step.set(order[i]);
  }

  protected canGoBack(): boolean {
    const step = this.step();
    if (step === 'done' || step === 'location') return false;
    return !(step === 'service' && (this.options()?.locations.length ?? 0) <= 1);
  }

  protected async moreDays(): Promise<void> {
    this.from.set(addDaysIso(this.from(), DAYS_PER_PAGE));
    await this.loadSlots(true);
  }

  protected async signIn(): Promise<void> {
    this.error.set(null);
    try {
      await this.auth.signInWithGoogle();
      await this.checkPhone();
    } catch {
      this.error.set('login.error');
    }
  }

  protected async confirm(): Promise<void> {
    const slot = this.slot();
    if (!slot) return;
    this.busy.set(true);
    this.error.set(null);

    if (this.needsPhone()) {
      const saved = await this.api.PUT('/api/users/me/phone', { body: { phoneNumber: this.phone.value } });
      if (saved.error) {
        this.error.set(`errors.${firstErrorCode(saved.error)}`);
        this.busy.set(false);
        return;
      }
      this.needsPhone.set(false);
    }

    const { data, error } = await this.api.POST('/api/scheduling/appointments', {
      headers: this.tenantHeader(),
      body: {
        locationId: this.location()!.id,
        serviceId: this.service()!.id,
        staffId: this.staff()?.id ?? null,
        startsAt: slot.startsAt,
      },
    });
    this.busy.set(false);

    if (data) {
      this.appointment.set(data);
      this.step.set('done');
      return;
    }

    // Typically the time was just taken: say so and offer fresh times.
    this.error.set(`errors.${firstErrorCode(error)}`);
    this.slot.set(null);
    await this.loadSlots(false);
    this.step.set('time');
  }

  protected bookAnother(): void {
    this.appointment.set(null);
    this.slot.set(null);
    this.service.set(null);
    this.step.set((this.options()?.locations.length ?? 0) > 1 ? 'location' : 'service');
  }

  // ---- data -------------------------------------------------------------------------------------------------
  private tenantHeader() {
    return { 'X-Tenant': this.slug() };
  }

  private async load(slug: string): Promise<void> {
    this.state.set('loading');
    const [tenant, options] = await Promise.all([
      // Public lookup by slug: never send the backoffice business selection.
      this.api.GET('/api/tenancy/tenants/{slug}', { params: { path: { slug } }, headers: { 'X-Tenant': '' } }),
      this.api.GET('/api/scheduling/booking-options', { headers: { 'X-Tenant': slug } }),
    ]);

    if (!tenant.data || !options.data) {
      this.state.set('notFound');
      return;
    }

    this.tenant.set(tenant.data);
    this.options.set(options.data);
    this.state.set('ready');

    const locations = options.data.locations;
    if (locations.length === 1) {
      this.chooseLocation(locations[0]);
    } else {
      this.step.set('location');
    }
  }

  private async goToTime(): Promise<void> {
    this.from.set(todayIso());
    this.slot.set(null);
    this.step.set('time');
    await this.loadSlots(false);
  }

  /** Loads DAYS_PER_PAGE days from `from` (appending when paging) and selects the first day with free times. */
  private async loadSlots(append: boolean): Promise<void> {
    this.loadingSlots.set(true);
    const { data } = await this.api.GET('/api/scheduling/availability', {
      headers: this.tenantHeader(),
      params: {
        query: {
          locationId: this.location()!.id,
          serviceId: this.service()!.id,
          staffId: this.staff()?.id,
          from: this.from(),
          days: DAYS_PER_PAGE,
        },
      },
    });
    this.loadingSlots.set(false);

    const days = data?.days ?? [];
    this.days.set(append ? [...this.days(), ...days] : days);
    const current = this.selectedDay();
    if (!append || !current || !this.days().some((d) => d.date === current && d.slots.length > 0)) {
      this.selectedDay.set(this.days().find((d) => d.slots.length > 0)?.date ?? null);
    }
    if (!append) this.revealSelectedDay();
  }

  /** Keeps the selected day chip visible in the horizontally scrolling strip. */
  private revealSelectedDay(): void {
    afterNextRender(
      () => {
        const strip = this.host.nativeElement.querySelector<HTMLElement>('.days');
        const chip = strip?.querySelector<HTMLElement>('.day.selected');
        if (strip && chip) strip.scrollLeft = Math.max(0, chip.offsetLeft - strip.offsetLeft - 16);
      },
      { injector: this.injector },
    );
  }

  private async checkPhone(): Promise<void> {
    if (!this.auth.isSignedIn()) return;
    const me = await this.session.load(true);
    this.needsPhone.set(!me?.phoneNumber);
  }
}
