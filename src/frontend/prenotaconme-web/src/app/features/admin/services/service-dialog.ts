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

type Service = Schemas['ServiceDto'];

export interface ServiceDialogData {
  service: Service | null;
  locations: Schemas['LocationDto'][];
}

@Component({
  selector: 'app-service-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ (data.service ? 'services.edit' : 'services.new') | transloco }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ 'common.name' | transloco }}</mat-label>
          <input matInput formControlName="name" maxlength="100" required />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'services.description' | transloco }}</mat-label>
          <textarea matInput formControlName="description" maxlength="500" rows="2"></textarea>
        </mat-form-field>
        <div class="row">
          <mat-form-field>
            <mat-label>{{ 'services.duration' | transloco }}</mat-label>
            <mat-select formControlName="durationMinutes">
              @for (m of durations; track m) {
                <mat-option [value]="m">{{ m }} min</mat-option>
              }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'services.price' | transloco }}</mat-label>
            <input matInput type="number" formControlName="price" min="0" step="0.5" />
            <span matTextSuffix>€</span>
          </mat-form-field>
        </div>
        <mat-form-field>
          <mat-label>{{ 'nav.locations' | transloco }}</mat-label>
          <mat-select formControlName="locationIds" multiple>
            @for (l of data.locations; track l.id) {
              <mat-option [value]="l.id">{{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-slide-toggle formControlName="isActive">{{ 'services.bookable' | transloco }}</mat-slide-toggle>
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
export class ServiceDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<ServiceDialog, boolean>);
  protected readonly data = inject<ServiceDialogData>(MAT_DIALOG_DATA);

  /** 30-minute grid, up to 8 hours (matches backend ServiceRules). */
  protected readonly durations = Array.from({ length: 16 }, (_, i) => (i + 1) * 30);
  protected readonly busy = signal(false);

  protected readonly form = new FormGroup({
    name: new FormControl(this.data.service?.name ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    description: new FormControl(this.data.service?.description ?? ''),
    durationMinutes: new FormControl(this.data.service?.durationMinutes ?? 30, { nonNullable: true }),
    price: new FormControl(this.data.service?.price ?? 0, { nonNullable: true, validators: [Validators.required, Validators.min(0)] }),
    locationIds: new FormControl<string[]>(
      this.data.service?.locationIds ?? (this.data.locations.length === 1 ? [this.data.locations[0].id] : []),
      { nonNullable: true },
    ),
    isActive: new FormControl(this.data.service?.isActive ?? true, { nonNullable: true }),
  });

  protected async save(): Promise<void> {
    this.busy.set(true);
    const v = this.form.getRawValue();
    const body: Schemas['SaveServiceRequest'] = { ...v, description: v.description || null, price: Number(v.price) };

    const { error } = this.data.service
      ? await this.api.PUT('/api/catalog/services/{id}', { params: { path: { id: this.data.service.id } }, body })
      : await this.api.POST('/api/catalog/services', { body });

    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
    } else {
      this.notifier.saved();
      this.ref.close(true);
    }
  }
}
