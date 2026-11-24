import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { localToIso, toLocalInputs } from './calendar-events';

export interface NewAppointmentDialogData {
  start: Date;
  locations: Schemas['LocationDto'][];
  services: Schemas['ServiceDto'][];
  staff: Schemas['CalendarStaffDto'][];
  /** Current calendar filters, used as defaults. */
  locationId: string | null;
  staffId: string | null;
}

/**
 * Admin enters an appointment for a client who phoned or walked in (no account needed). Working hours are not
 * enforced here; the server only refuses overlaps with the same person. Closes with true when created.
 */
@Component({
  selector: 'app-new-appointment-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ 'calendar.new' | transloco }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        @if (data.locations.length > 1) {
          <mat-form-field>
            <mat-label>{{ 'calendar.location' | transloco }}</mat-label>
            <mat-select formControlName="locationId">
              @for (l of data.locations; track l.id) {
                <mat-option [value]="l.id">{{ l.name }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
        <mat-form-field>
          <mat-label>{{ 'calendar.service' | transloco }}</mat-label>
          <mat-select formControlName="serviceId">
            @for (s of services(); track s.id) {
              <mat-option [value]="s.id">{{ s.name }} · {{ s.durationMinutes }} min</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'calendar.staff' | transloco }}</mat-label>
          <mat-select formControlName="staffId">
            @for (s of staff(); track s.id) {
              <mat-option [value]="s.id">{{ s.displayName }}</mat-option>
            }
          </mat-select>
          @if (serviceId() && staff().length === 0) {
            <mat-hint>{{ 'calendar.noStaffForService' | transloco }}</mat-hint>
          }
        </mat-form-field>
        <div class="row">
          <mat-form-field>
            <mat-label>{{ 'calendar.date' | transloco }}</mat-label>
            <input matInput type="date" formControlName="date" required />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'calendar.time' | transloco }}</mat-label>
            <input matInput type="time" step="900" formControlName="time" required />
          </mat-form-field>
        </div>
        <mat-form-field>
          <mat-label>{{ 'calendar.clientName' | transloco }}</mat-label>
          <input matInput formControlName="clientName" maxlength="100" required autocomplete="off" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'calendar.clientPhone' | transloco }}</mat-label>
          <input matInput type="tel" formControlName="clientPhone" maxlength="32" autocomplete="off" />
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'common.save' | transloco }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field { width: 100%; }
    .row { display: flex; gap: 12px; }
    .row mat-form-field { flex: 1; }
  `,
})
export class NewAppointmentDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<NewAppointmentDialog, boolean>);
  protected readonly data = inject<NewAppointmentDialogData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);

  private readonly start = toLocalInputs(this.data.start);

  protected readonly form = new FormGroup({
    locationId: new FormControl(this.data.locationId ?? this.data.locations[0]?.id ?? '', { nonNullable: true, validators: [Validators.required] }),
    serviceId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    staffId: new FormControl(this.data.staffId ?? '', { nonNullable: true, validators: [Validators.required] }),
    date: new FormControl(this.start.date, { nonNullable: true, validators: [Validators.required] }),
    time: new FormControl(this.start.time, { nonNullable: true, validators: [Validators.required] }),
    clientName: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    clientPhone: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(32)] }),
  });

  private readonly locationId = toSignal(this.form.controls.locationId.valueChanges, { initialValue: this.form.controls.locationId.value });
  protected readonly serviceId = toSignal(this.form.controls.serviceId.valueChanges, { initialValue: '' });

  protected readonly services = computed(() =>
    this.data.services.filter((s) => s.isActive && s.locationIds.includes(this.locationId())),
  );

  protected readonly staff = computed(() =>
    this.data.staff.filter((p) => p.locationIds.includes(this.locationId()) && p.serviceIds.includes(this.serviceId())),
  );

  constructor() {
    // Drop a chosen person who does not perform the newly chosen service.
    this.form.controls.serviceId.valueChanges.subscribe(() => {
      const staffId = this.form.controls.staffId.value;
      const eligible = this.staff();
      if (!eligible.some((p) => p.id === staffId)) {
        this.form.controls.staffId.setValue(eligible.length === 1 ? eligible[0].id : '');
      }
    });
  }

  protected async save(): Promise<void> {
    const v = this.form.getRawValue();
    this.busy.set(true);
    const { error } = await this.api.POST('/api/scheduling/calendar/appointments', {
      body: {
        locationId: v.locationId,
        serviceId: v.serviceId,
        staffId: v.staffId,
        startsAt: localToIso(v.date, v.time),
        clientName: v.clientName,
        clientPhone: v.clientPhone || null,
      },
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
