import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { DayOfWeek, hoursToRanges, rangesToHours, toMinutes, WEEK_DAYS } from '../../../shared/time';

type Location = Schemas['LocationDto'];
type Cells = Map<DayOfWeek, Set<number>>;

/**
 * F1-07: weekly availability in 1-hour blocks per location (click cells to toggle) and full days off.
 * Cells outside the location's opening hours are disabled; hours already used at another location are blocked.
 */
@Component({
  selector: 'app-staff-schedule-page',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatIconModule, MatInputModule, MatTabsModule, TranslocoPipe],
  template: `
    <a mat-button routerLink="/admin/staff"><mat-icon>arrow_back</mat-icon>{{ 'nav.staff' | transloco }}</a>
    <h1>{{ 'staff.scheduleOf' | transloco: { name: staff()?.displayName ?? '' } }}</h1>

    <mat-card appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'staff.workingHours' | transloco }}</mat-card-title>
        <mat-card-subtitle>{{ 'staff.workingHoursHint' | transloco }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (locations().length === 0) {
          <p>{{ 'staff.noLocations' | transloco }}</p>
        } @else {
          <mat-tab-group [selectedIndex]="tab()" (selectedIndexChange)="tab.set($event)">
            @for (l of locations(); track l.id) {
              <mat-tab [label]="l.name" />
            }
          </mat-tab-group>

          @if (current(); as loc) {
            <div class="grid-wrap">
              <table class="grid">
                <thead>
                  <tr>
                    <th></th>
                    @for (h of hourColumns(); track h) {
                      <th>{{ h }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (day of days; track day) {
                    <tr>
                      <th>
                        <button mat-button class="day" (click)="toggleDay(loc, day)" [title]="'staff.toggleDay' | transloco">
                          {{ 'days.' + day | transloco }}
                        </button>
                      </th>
                      @for (h of hourColumns(); track h) {
                        @let state = cellState(loc, day, h);
                        <td>
                          <button
                            class="cell"
                            [class.on]="state === 'on'"
                            [class.elsewhere]="state === 'elsewhere'"
                            [disabled]="state === 'closed' || state === 'elsewhere'"
                            [attr.aria-pressed]="state === 'on'"
                            [attr.aria-label]="('days.' + day | transloco) + ' ' + h + ':00'"
                            (click)="toggle(loc.id, day, h)"
                          ></button>
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p class="legend">
              <span class="cell on"></span> {{ 'staff.legendWorking' | transloco }}
              <span class="cell"></span> {{ 'staff.legendFree' | transloco }}
              <span class="cell elsewhere"></span> {{ 'staff.legendElsewhere' | transloco }}
            </p>
          }
        }
      </mat-card-content>
      <mat-card-actions align="end">
        <button mat-flat-button (click)="saveHours()" [disabled]="busy() || !dirty()">{{ 'common.save' | transloco }}</button>
      </mat-card-actions>
    </mat-card>

    <mat-card appearance="outlined" class="time-off">
      <mat-card-header>
        <mat-card-title>{{ 'staff.timeOff' | transloco }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <ul>
          @for (t of timeOff(); track t.id) {
            <li>
              {{ t.from }} → {{ t.to }} @if (t.reason) { · {{ t.reason }} }
              <button mat-icon-button (click)="deleteTimeOff(t.id)" [attr.aria-label]="'common.delete' | transloco"><mat-icon>delete</mat-icon></button>
            </li>
          } @empty {
            <li class="muted">{{ 'staff.noTimeOff' | transloco }}</li>
          }
        </ul>
        <form [formGroup]="timeOffForm" (ngSubmit)="addTimeOff()" class="time-off-form">
          <mat-form-field>
            <mat-label>{{ 'staff.from' | transloco }}</mat-label>
            <input matInput type="date" formControlName="from" required />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'staff.to' | transloco }}</mat-label>
            <input matInput type="date" formControlName="to" required />
          </mat-form-field>
          <mat-form-field class="reason">
            <mat-label>{{ 'staff.reason' | transloco }}</mat-label>
            <input matInput formControlName="reason" maxlength="200" />
          </mat-form-field>
          <button mat-stroked-button type="submit" [disabled]="timeOffForm.invalid">{{ 'staff.addTimeOff' | transloco }}</button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .grid-wrap { overflow-x: auto; margin-top: 12px; }
    .grid { border-collapse: separate; border-spacing: 2px; }
    .grid th { font: var(--mat-sys-label-small); color: var(--mat-sys-on-surface-variant); text-align: center; }
    .grid tbody th { text-align: left; }
    .day { min-width: 96px; justify-content: flex-start; }
    .cell { width: 32px; height: 28px; border-radius: 4px; border: 1px solid var(--mat-sys-outline-variant); background: var(--mat-sys-surface); cursor: pointer; padding: 0; display: inline-block; vertical-align: middle; }
    .cell.on { background: var(--mat-sys-primary); border-color: var(--mat-sys-primary); }
    .cell.elsewhere { background: var(--mat-sys-tertiary-container); cursor: not-allowed; }
    .cell:disabled:not(.elsewhere) { background: var(--mat-sys-surface-container-highest); border-color: transparent; cursor: default; }
    .legend { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; color: var(--mat-sys-on-surface-variant); }
    .legend .cell { cursor: default; margin-left: 12px; }
    .time-off { margin-top: 16px; }
    .time-off ul { padding-left: 16px; }
    .time-off li { display: flex; align-items: center; gap: 8px; }
    .time-off-form { display: flex; gap: 12px; flex-wrap: wrap; align-items: baseline; }
    .reason { flex: 1 1 200px; }
    .muted { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class StaffSchedulePage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);

  /** Route param :id */
  readonly id = input.required<string>();

  protected readonly days = WEEK_DAYS;
  protected readonly staff = signal<Schemas['StaffDto'] | null>(null);
  protected readonly locations = signal<Location[]>([]);
  protected readonly timeOff = signal<Schemas['TimeOffDto'][]>([]);
  protected readonly tab = signal(0);
  protected readonly busy = signal(false);
  protected readonly dirty = signal(false);

  /** Selected hours per location. Replaced (not mutated) on change so computeds update. */
  private readonly cells = signal(new Map<string, Cells>());

  protected readonly current = computed(() => this.locations()[this.tab()] ?? null);

  /** Hour columns spanning the location's earliest opening to latest closing. */
  protected readonly hourColumns = computed(() => {
    const loc = this.current();
    if (!loc || loc.openingHours.length === 0) return [];
    const first = Math.floor(Math.min(...loc.openingHours.map((h) => toMinutes(h.opens))) / 60);
    const last = Math.ceil(Math.max(...loc.openingHours.map((h) => toMinutes(h.closes))) / 60);
    return Array.from({ length: last - first }, (_, i) => first + i);
  });

  protected readonly timeOffForm = new FormGroup({
    from: new FormControl('', { nonNullable: true, validators: Validators.required }),
    to: new FormControl('', { nonNullable: true, validators: Validators.required }),
    reason: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    void this.load();
  }

  protected cellState(loc: Location, day: DayOfWeek, hour: number): 'on' | 'off' | 'closed' | 'elsewhere' {
    if (this.cells().get(loc.id)?.get(day)?.has(hour)) return 'on';
    if (!isOpen(loc, day, hour)) return 'closed';
    for (const [otherId, other] of this.cells()) {
      if (otherId !== loc.id && other.get(day)?.has(hour)) return 'elsewhere';
    }
    return 'off';
  }

  protected toggle(locationId: string, day: DayOfWeek, hour: number): void {
    this.update(locationId, day, (set) => (set.has(hour) ? set.delete(hour) : set.add(hour)));
  }

  /** Fills the whole opening hours of a day, or clears it if it is already full. */
  protected toggleDay(loc: Location, day: DayOfWeek): void {
    const free = this.hourColumns().filter((h) => ['on', 'off'].includes(this.cellState(loc, day, h)));
    const allOn = free.every((h) => this.cellState(loc, day, h) === 'on');
    this.update(loc.id, day, (set) => free.forEach((h) => (allOn ? set.delete(h) : set.add(h))));
  }

  protected async saveHours(): Promise<void> {
    this.busy.set(true);
    const body = [...this.cells()].flatMap(([locationId, cells]) =>
      hoursToRanges(cells).map((r) => ({
        locationId,
        day: r.day,
        start: `${String(r.start).padStart(2, '0')}:00:00`,
        end: `${String(r.end).padStart(2, '0')}:00:00`,
      })),
    );

    const { error } = await this.api.PUT('/api/staff/{id}/working-hours', { params: { path: { id: this.id() } }, body });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
    } else {
      this.dirty.set(false);
      this.notifier.saved();
    }
  }

  protected async addTimeOff(): Promise<void> {
    const v = this.timeOffForm.getRawValue();
    const { error } = await this.api.POST('/api/staff/{id}/time-off', {
      params: { path: { id: this.id() } },
      body: { from: v.from, to: v.to, reason: v.reason || null },
    });
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.timeOffForm.reset();
    await this.loadSchedule();
  }

  protected async deleteTimeOff(timeOffId: string): Promise<void> {
    const { error } = await this.api.DELETE('/api/staff/{id}/time-off/{timeOffId}', { params: { path: { id: this.id(), timeOffId } } });
    if (error) this.notifier.error(error);
    await this.loadSchedule();
  }

  private update(locationId: string, day: DayOfWeek, change: (set: Set<number>) => void): void {
    const next = new Map(this.cells());
    const byDay = new Map(next.get(locationId) ?? []);
    const set = new Set(byDay.get(day) ?? []);
    change(set);
    byDay.set(day, set);
    next.set(locationId, byDay);
    this.cells.set(next);
    this.dirty.set(true);
  }

  private async load(): Promise<void> {
    const [staff, locations] = await Promise.all([
      this.api.GET('/api/staff/{id}', { params: { path: { id: this.id() } } }),
      this.api.GET('/api/locations'),
    ]);
    this.staff.set(staff.data ?? null);
    const assigned = new Set(staff.data?.locationIds ?? []);
    this.locations.set((locations.data ?? []).filter((l) => assigned.has(l.id)));
    await this.loadSchedule();
  }

  private async loadSchedule(): Promise<void> {
    const { data } = await this.api.GET('/api/staff/{id}/schedule', { params: { path: { id: this.id() } } });
    const cells = new Map<string, Cells>();
    for (const loc of this.locations()) {
      cells.set(loc.id, rangesToHours((data?.workingHours ?? []).filter((h) => h.locationId === loc.id)));
    }
    if (!this.dirty()) this.cells.set(cells);
    this.timeOff.set(data?.timeOff ?? []);
  }
}

/** The whole hour [hour, hour+1) lies inside one of the day's opening ranges. */
function isOpen(loc: Location, day: DayOfWeek, hour: number): boolean {
  return loc.openingHours.some((h) => h.day === day && toMinutes(h.opens) <= hour * 60 && (hour + 1) * 60 <= toMinutes(h.closes));
}
