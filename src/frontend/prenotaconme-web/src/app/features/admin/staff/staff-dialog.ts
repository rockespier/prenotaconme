import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';

export interface StaffDialogData {
  staff: Schemas['StaffDto'] | null;
  locations: Schemas['LocationDto'][];
  services: Schemas['ServiceDto'][];
}

@Component({
  selector: 'app-staff-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ (data.staff ? 'staff.edit' : 'staff.new') | transloco }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ 'common.name' | transloco }}</mat-label>
          <input matInput formControlName="displayName" maxlength="100" required />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'staff.email' | transloco }}</mat-label>
          <input matInput type="email" formControlName="email" required />
          <mat-hint>{{ (data.staff?.isLinked ? 'staff.emailLocked' : 'staff.emailHint') | transloco }}</mat-hint>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'nav.locations' | transloco }}</mat-label>
          <mat-select formControlName="locationIds" multiple>
            @for (l of data.locations; track l.id) {
              <mat-option [value]="l.id">{{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'nav.services' | transloco }}</mat-label>
          <mat-select formControlName="serviceIds" multiple>
            @for (s of data.services; track s.id) {
              <mat-option [value]="s.id">{{ s.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-slide-toggle formControlName="isActive">{{ 'staff.active' | transloco }}</mat-slide-toggle>
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">{{ 'common.save' | transloco }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `mat-form-field { width: 100%; margin-bottom: 4px; }`,
})
export class StaffDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<StaffDialog, boolean>);
  protected readonly data = inject<StaffDialogData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);

  protected readonly form = new FormGroup({
    displayName: new FormControl(this.data.staff?.displayName ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    email: new FormControl(
      { value: this.data.staff?.email ?? '', disabled: this.data.staff?.isLinked ?? false },
      { nonNullable: true, validators: [Validators.required, Validators.email] },
    ),
    locationIds: new FormControl<string[]>(
      this.data.staff?.locationIds ?? (this.data.locations.length === 1 ? [this.data.locations[0].id] : []),
      { nonNullable: true },
    ),
    serviceIds: new FormControl<string[]>(this.data.staff?.serviceIds ?? this.data.services.map((s) => s.id), { nonNullable: true }),
    isActive: new FormControl(this.data.staff?.isActive ?? true, { nonNullable: true }),
  });

  protected async save(): Promise<void> {
    this.busy.set(true);
    const body = this.form.getRawValue();
    const { error } = this.data.staff
      ? await this.api.PUT('/api/staff/{id}', { params: { path: { id: this.data.staff.id } }, body })
      : await this.api.POST('/api/staff', { body });

    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
    } else {
      this.notifier.saved();
      this.ref.close(true);
    }
  }
}
