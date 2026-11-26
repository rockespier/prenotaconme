import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, DestroyRef, ElementRef, afterNextRender, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { Calendar, type DateSelectArg, type EventClickArg, type EventDropArg, type EventSourceFuncArg, type EventInput } from '@fullcalendar/core';
import enGbLocale from '@fullcalendar/core/locales/en-gb';
import esLocale from '@fullcalendar/core/locales/es';
import itLocale from '@fullcalendar/core/locales/it';
import interactionPlugin from '@fullcalendar/interaction';
import timeGridPlugin from '@fullcalendar/timegrid';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { SessionService } from '../../../core/session/session.service';
import { TenantSelection } from '../../../core/session/tenant-selection';
import { Notifier } from '../../../shared/api-errors';
import { AppointmentDialog, AppointmentDialogData } from './appointment-dialog';
import { staffColor, toEvents } from './calendar-events';
import { connectCalendarLive } from './calendar-live';
import { NewAppointmentDialog, NewAppointmentDialogData } from './new-appointment-dialog';

type Appointment = Schemas['CalendarAppointmentDto'];
type Staff = Schemas['CalendarStaffDto'];

const LOCALES: Record<string, string> = { it: 'it', es: 'es', en: 'en-gb' };

/**
 * F1-13: day/week calendar of appointments (browser time zone). Admins filter by location and staff, create
 * appointments by selecting a free range, move them by drag & drop and cancel them; Staff see only their own.
 */
@Component({
  selector: 'app-calendar-page',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.calendar' | transloco }}</h1>
      @if (session.isAdmin()) {
        <button mat-flat-button (click)="create()"><mat-icon>add</mat-icon>{{ 'calendar.new' | transloco }}</button>
      }
    </header>

    <div class="filters">
      @if (locations().length > 1) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'calendar.location' | transloco }}</mat-label>
          <mat-select [value]="locationId()" (valueChange)="locationId.set($event)">
            <mat-option [value]="null">{{ 'calendar.all' | transloco }}</mat-option>
            @for (l of locations(); track l.id) {
              <mat-option [value]="l.id">{{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      @if (staff().length > 1) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ 'calendar.staff' | transloco }}</mat-label>
          <mat-select [value]="staffId()" (valueChange)="staffId.set($event)">
            <mat-option [value]="null">{{ 'calendar.all' | transloco }}</mat-option>
            @for (s of staff(); track s.id) {
              <mat-option [value]="s.id"><span class="dot" [style.background]="color(s.id)"></span>{{ s.displayName }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      @if (staff().length > 1) {
        <ul class="legend" aria-hidden="true">
          @for (s of staff(); track s.id) {
            <li><span class="dot" [style.background]="color(s.id)"></span>{{ s.displayName }}</li>
          }
        </ul>
      }
    </div>

    @if (!session.isAdmin() && loaded() && staff().length === 0) {
      <p class="muted">{{ 'calendar.notLinked' | transloco }}</p>
    }

    <div #calendar class="calendar"></div>
  `,
  styles: `
    :host { display: block; }
    .filters { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); margin-bottom: var(--space-4); }
    .filters mat-form-field { width: 220px; }
    .legend { display: flex; flex-wrap: wrap; gap: var(--space-3); list-style: none; margin: 0; padding: 0; font: var(--mat-sys-body-small); }
    .legend li { display: inline-flex; align-items: center; gap: 6px; }
    .dot { display: inline-block; width: 10px; height: 10px; margin-right: 6px; border-radius: 50%; vertical-align: middle; }
    .legend .dot { margin-right: 0; }

    .calendar {
      --fc-border-color: var(--mat-sys-outline-variant);
      --fc-today-bg-color: color-mix(in srgb, var(--mat-sys-primary) 6%, transparent);
      --fc-now-indicator-color: var(--mat-sys-error);
      --fc-button-bg-color: var(--mat-sys-surface);
      --fc-button-border-color: var(--mat-sys-outline-variant);
      --fc-button-text-color: var(--mat-sys-on-surface);
      --fc-button-hover-bg-color: var(--mat-sys-surface-container-high);
      --fc-button-hover-border-color: var(--mat-sys-outline-variant);
      --fc-button-active-bg-color: var(--mat-sys-primary-container);
      --fc-button-active-border-color: var(--mat-sys-primary);
      --fc-page-bg-color: var(--mat-sys-surface);
      font: var(--mat-sys-body-medium);
    }
    :host ::ng-deep .fc .fc-toolbar-title { font: var(--mat-sys-title-large); text-transform: capitalize; }
    :host ::ng-deep .fc .fc-button { text-transform: capitalize; box-shadow: none; }
    :host ::ng-deep .fc .fc-event { cursor: pointer; }
    :host ::ng-deep .fc .fc-timegrid-slot { height: 2.25em; }
    @media (max-width: 599px) {
      :host ::ng-deep .fc .fc-toolbar { flex-wrap: wrap; gap: var(--space-2); }
      :host ::ng-deep .fc .fc-toolbar-title { font: var(--mat-sys-title-medium); }
    }
  `,
})
export class CalendarPage {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);
  private readonly breakpoints = inject(BreakpointObserver);
  protected readonly session = inject(SessionService);
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantSelection);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('calendar');
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });
  private calendar: Calendar | null = null;

  protected readonly locations = signal<Schemas['LocationDto'][]>([]);
  protected readonly services = signal<Schemas['ServiceDto'][]>([]);
  protected readonly staff = signal<Staff[]>([]);
  protected readonly locationId = signal<string | null>(null);
  protected readonly staffId = signal<string | null>(null);
  protected readonly loaded = signal(false);

  constructor() {
    afterNextRender(() => this.init());
    inject(DestroyRef).onDestroy(() => this.calendar?.destroy());

    effect(() => {
      const locale = LOCALES[this.lang()] ?? 'it';
      untracked(() => this.calendar?.setOption('locale', locale));
    });

    // F2-10: someone else changed an appointment of this business (or the client booked): reload the visible range.
    effect((onCleanup) => {
      const slug = this.tenant.slug();
      if (!slug) return;
      onCleanup(connectCalendarLive(this.auth, slug, () => this.calendar?.refetchEvents()));
    });

    // Filters changed: reload the visible range.
    effect(() => {
      this.locationId();
      this.staffId();
      untracked(() => this.calendar?.refetchEvents());
    });
  }

  protected color = (id: string) => staffColor(this.staff(), id);

  protected async create(start?: Date): Promise<void> {
    const data: NewAppointmentDialogData = {
      start: start ?? this.nextQuarterHour(),
      locations: this.locations().filter((l) => l.isActive),
      services: this.services(),
      staff: this.staff().filter((s) => s.isActive),
      locationId: this.locationId(),
      staffId: this.staffId(),
    };
    const saved = await firstValueFrom(
      this.dialog.open(NewAppointmentDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed(),
    );
    if (saved) this.calendar?.refetchEvents();
  }

  private init(): void {
    const isAdmin = this.session.isAdmin();
    const small = this.breakpoints.isMatched('(max-width: 767px)');

    this.calendar = new Calendar(this.host().nativeElement, {
      plugins: [timeGridPlugin, interactionPlugin],
      locales: [itLocale, esLocale, enGbLocale],
      locale: LOCALES[this.lang()] ?? 'it',
      initialView: small ? 'timeGridDay' : 'timeGridWeek',
      headerToolbar: { left: 'prev,next today', center: 'title', right: 'timeGridDay,timeGridWeek' },
      firstDay: 1,
      allDaySlot: false,
      nowIndicator: true,
      slotMinTime: '07:00:00',
      slotMaxTime: '22:00:00',
      scrollTime: '08:00:00',
      slotDuration: '00:30:00',
      snapDuration: '00:15:00',
      height: 'auto',
      expandRows: true,
      // A 30-minute appointment is ~32 px tall: render time and client on one line so the name is never cut off.
      eventShortHeight: 44,
      editable: isAdmin,
      eventDurationEditable: false,
      selectable: isAdmin,
      selectMirror: true,
      eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
      slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
      events: (info, success, failure) => {
        this.fetch(info).then(success, failure);
      },
      eventDrop: (info) => void this.move(info),
      eventClick: (info) => void this.open(info),
      select: (info: DateSelectArg) => {
        this.calendar?.unselect();
        void this.create(info.start);
      },
    });
    this.calendar.render();

    if (isAdmin) void this.loadCatalog();
  }

  private async loadCatalog(): Promise<void> {
    const [locations, services] = await Promise.all([this.api.GET('/api/locations'), this.api.GET('/api/catalog/services')]);
    this.locations.set(locations.data ?? []);
    this.services.set(services.data ?? []);
  }

  private async fetch(info: EventSourceFuncArg): Promise<EventInput[]> {
    const { data, error } = await this.api.GET('/api/scheduling/calendar', {
      params: {
        query: {
          from: info.start.toISOString(),
          to: info.end.toISOString(),
          locationId: this.locationId() ?? undefined,
          staffId: this.staffId() ?? undefined,
        },
      },
    });
    if (!data) {
      this.notifier.error(error);
      return [];
    }
    this.staff.set(data.staff);
    this.loaded.set(true);
    return toEvents(data.appointments, data.staff);
  }

  /** Drag & drop: same person, new start. The server refuses overlaps (409) and the event jumps back. */
  private async move(info: EventDropArg): Promise<void> {
    const a = info.event.extendedProps['appointment'] as Appointment;
    const { error } = await this.api.PUT('/api/scheduling/calendar/appointments/{id}', {
      params: { path: { id: a.id } },
      body: { staffId: a.staffMemberId, startsAt: info.event.start!.toISOString() },
    });
    if (error) {
      info.revert();
      this.notifier.error(error);
      return;
    }
    this.notifier.saved();
    this.calendar?.refetchEvents();
  }

  private async open(info: EventClickArg): Promise<void> {
    const data: AppointmentDialogData = {
      appointment: info.event.extendedProps['appointment'] as Appointment,
      staff: this.staff(),
      locations: this.locations(),
      canEdit: this.session.isAdmin(),
    };
    const changed = await firstValueFrom(
      this.dialog.open(AppointmentDialog, { data, width: '480px', maxWidth: '95vw' }).afterClosed(),
    );
    if (changed) this.calendar?.refetchEvents();
  }

  private nextQuarterHour(): Date {
    const now = new Date();
    now.setMinutes(Math.ceil((now.getMinutes() + 1) / 15) * 15, 0, 0);
    return now;
  }
}
