import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { addDaysIso, todayIso } from '../../booking/booking-format';

export interface AnnouncementDialogData {
  announcement: Schemas['AnnouncementDto'] | null;
  audience: Schemas['AnnouncementAudienceDto'] | null;
}

/** F2-03: write or edit an announcement; a new one can be sent to the clients right away. */
@Component({
  selector: 'app-announcement-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ (data.announcement ? 'announcements.edit' : 'announcements.new') | transloco }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>{{ 'announcements.titleLabel' | transloco }}</mat-label>
          <input matInput formControlName="title" maxlength="100" required [placeholder]="'announcements.titlePlaceholder' | transloco" />
        </mat-form-field>
        <mat-form-field>
          <mat-label>{{ 'announcements.message' | transloco }}</mat-label>
          <textarea matInput formControlName="message" maxlength="1000" rows="5" required
            [placeholder]="'announcements.messagePlaceholder' | transloco"></textarea>
          <mat-hint align="end">{{ form.controls.message.value.length }}/1000</mat-hint>
        </mat-form-field>
        <div class="row">
          <mat-form-field>
            <mat-label>{{ 'announcements.from' | transloco }}</mat-label>
            <input matInput type="date" formControlName="startsOn" required />
          </mat-form-field>
          <mat-form-field>
            <mat-label>{{ 'announcements.to' | transloco }}</mat-label>
            <input matInput type="date" formControlName="endsOn" required [min]="form.controls.startsOn.value" />
          </mat-form-field>
        </div>
        <p class="muted hint">{{ 'announcements.periodHint' | transloco }}</p>
        @if (!data.announcement) {
          <mat-checkbox formControlName="notify">
            {{ 'announcements.notify' | transloco: { count: data.audience?.clients ?? 0 } }}
          </mat-checkbox>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | transloco }}</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || busy()">
          {{ (form.controls.notify.value && !data.announcement ? 'announcements.saveAndSend' : 'common.save') | transloco }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field { width: 100%; }
    .row { display: flex; gap: 12px; }
    .row mat-form-field { flex: 1; }
    .hint { margin: 0 0 var(--space-3); font: var(--mat-sys-body-small); }
  `,
})
export class AnnouncementDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<AnnouncementDialog, boolean>);
  protected readonly data = inject<AnnouncementDialogData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);

  protected readonly form = new FormGroup({
    title: new FormControl(this.data.announcement?.title ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    message: new FormControl(this.data.announcement?.message ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] }),
    startsOn: new FormControl(this.data.announcement?.startsOn ?? todayIso(), { nonNullable: true, validators: Validators.required }),
    endsOn: new FormControl(this.data.announcement?.endsOn ?? addDaysIso(todayIso(), 7), { nonNullable: true, validators: Validators.required }),
    notify: new FormControl(false, { nonNullable: true }),
  });

  protected async save(): Promise<void> {
    this.busy.set(true);
    const { notify, ...v } = this.form.getRawValue();
    const { error } = this.data.announcement
      ? await this.api.PUT('/api/notifications/announcements/{id}', { params: { path: { id: this.data.announcement.id } }, body: v })
      : await this.api.POST('/api/notifications/announcements', { body: { ...v, notify } });

    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
    } else {
      this.notifier.saved();
      this.ref.close(true);
    }
  }
}
