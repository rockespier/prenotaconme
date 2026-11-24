import { Component, inject, signal } from '@angular/core';
import { FormArray, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { DayOfWeek, toApiTime, toHm, WEEK_DAYS } from '../../../shared/time';

type Location = Schemas['LocationDto'];

const rangeGroup = (opens = '09:00', closes = '19:00') =>
  new FormGroup({
    opens: new FormControl(opens, { nonNullable: true, validators: Validators.required }),
    closes: new FormControl(closes, { nonNullable: true, validators: Validators.required }),
  });

@Component({
  selector: 'app-location-dialog',
  imports: [
    ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule,
    MatInputModule, MatSelectModule, MatSlideToggleModule, TranslocoPipe,
  ],
  template: `
    <h2 mat-dialog-title>{{ (data ? 'locations.edit' : 'locations.new') | transloco }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ 'common.name' | transloco }}</mat-label>
          <input matInput formControlName="name" maxlength="100" required />
        </mat-form-field>
        <div class="row">
          <mat-form-field>
            <mat-label>{{ 'locations.address' | transloco }}</mat-label>
            <input matInput formControlName="address" maxlength="200" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'locations.city' | transloco }}</mat-label>
            <input matInput formControlName="city" maxlength="100" />
          </mat-form-field>
        </div>
        <div class="row">
          <mat-form-field>
            <mat-label>{{ 'locations.phone' | transloco }}</mat-label>
            <input matInput formControlName="phone" maxlength="32" type="tel" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'register.timeZone' | transloco }}</mat-label>
            <mat-select formControlName="timeZone">
              @for (tz of timeZones; track tz) {
                <mat-option [value]="tz">{{ tz }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        </div>
        <mat-slide-toggle formControlName="isActive">{{ 'locations.active' | transloco }}</mat-slide-toggle>

        <h3>{{ 'locations.openingHours' | transloco }}</h3>
        @for (day of days; track day) {
          <div class="day">
            <span class="day-name">{{ 'days.' + day | transloco }}</span>
            <div class="ranges">
              @for (range of hours[day].controls; track $index; let i = $index) {
                <span class="range" [formGroup]="range">
                  <input type="time" step="1800" formControlName="opens" [attr.aria-label]="'locations.opens' | transloco" />
                  –
                  <input type="time" step="1800" formControlName="closes" [attr.aria-label]="'locations.closes' | transloco" />
                  <button mat-icon-button type="button" (click)="hours[day].removeAt(i)" [attr.aria-label]="'common.delete' | transloco">
                    <mat-icon>close</mat-icon>
                  </button>
                </span>
              } @empty {
                <span class="closed">{{ 'locations.closed' | transloco }}</span>
              }
            </div>
            <button mat-icon-button type="button" (click)="hours[day].push(rangeGroup())" [attr.aria-label]="'locations.addRange' | transloco">
              <mat-icon>add</mat-icon>
            </button>
          </div>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'common.save' | transloco }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field { width: 100%; }
    .row { display: flex; gap: 12px; flex-wrap: wrap; }
    .row mat-form-field { flex: 1 1 200px; }
    h3 { margin: 16px 0 4px; font: var(--mat-sys-title-small); }
    .day { display: flex; align-items: center; gap: 8px; min-height: 44px; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .day-name { width: 96px; }
    .ranges { flex: 1; display: flex; flex-wrap: wrap; gap: 4px 12px; }
    .range { display: inline-flex; align-items: center; gap: 4px; }
    .range input { font: inherit; padding: 4px; }
    .closed { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class LocationDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<LocationDialog, boolean>);
  protected readonly data = inject<Location | null>(MAT_DIALOG_DATA);

  protected readonly days = WEEK_DAYS;
  protected readonly rangeGroup = rangeGroup;
  protected readonly timeZones = (Intl as typeof Intl & { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.('timeZone') ?? ['Europe/Rome'];
  protected readonly busy = signal(false);

  protected readonly hours = Object.fromEntries(
    WEEK_DAYS.map((day) => [
      day,
      new FormArray((this.data?.openingHours ?? []).filter((h) => h.day === day).map((h) => rangeGroup(toHm(h.opens), toHm(h.closes)))),
    ]),
  ) as Record<DayOfWeek, FormArray<ReturnType<typeof rangeGroup>>>;

  protected readonly form = new FormGroup({
    name: new FormControl(this.data?.name ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    address: new FormControl(this.data?.address ?? ''),
    city: new FormControl(this.data?.city ?? ''),
    phone: new FormControl(this.data?.phone ?? ''),
    timeZone: new FormControl(this.data?.timeZone ?? (Intl.DateTimeFormat().resolvedOptions().timeZone || 'Europe/Rome'), { nonNullable: true }),
    isActive: new FormControl(this.data?.isActive ?? true, { nonNullable: true }),
    openingHours: new FormGroup(this.hours),
  });

  protected async save(): Promise<void> {
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body: Schemas['SaveLocationRequest'] = {
      name: v.name,
      address: v.address || null,
      city: v.city || null,
      phone: v.phone || null,
      timeZone: v.timeZone,
      isActive: v.isActive,
      openingHours: WEEK_DAYS.flatMap((day) =>
        this.hours[day].getRawValue().map((r) => ({ day, opens: toApiTime(r.opens), closes: toApiTime(r.closes) })),
      ),
    };

    const { error } = this.data
      ? await this.api.PUT('/api/locations/{id}', { params: { path: { id: this.data.id } }, body })
      : await this.api.POST('/api/locations', { body });

    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
    } else {
      this.notifier.saved();
      this.ref.close(true);
    }
  }
}
