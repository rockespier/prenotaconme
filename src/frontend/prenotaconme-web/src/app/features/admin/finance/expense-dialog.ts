import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';

type Expense = Schemas['ExpenseDto'];
type Kind = Expense['kind'];

export interface ExpenseDialogData {
  expense: Expense | null;
  locations: Schemas['LocationDto'][];
  /** yyyy-MM-dd used as default date for a new expense. */
  defaultDate: string;
}

/** F4-02: add or edit an expense. Fixed ones repeat every month (from/to month); variable ones happen on a day. */
@Component({
  selector: 'app-expense-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  template: `
    <h2 mat-dialog-title>{{ (data.expense ? 'finance.expenses.edit' : 'finance.expenses.new') | transloco }}</h2>
    <mat-dialog-content [formGroup]="form">
      <mat-button-toggle-group formControlName="kind" class="kind">
        <mat-button-toggle value="Fixed">{{ 'finance.expenses.Fixed' | transloco }}</mat-button-toggle>
        <mat-button-toggle value="Variable">{{ 'finance.expenses.Variable' | transloco }}</mat-button-toggle>
      </mat-button-toggle-group>
      <p class="muted hint">{{ 'finance.expenses.hint' + form.controls.kind.value | transloco }}</p>

      <mat-form-field appearance="outline">
        <mat-label>{{ 'finance.expenses.name' | transloco }}</mat-label>
        <input matInput formControlName="name" maxlength="100" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>{{ 'finance.expenses.amount' | transloco }}</mat-label>
        <input matInput type="number" min="0" step="0.01" formControlName="amount" />
        <span matTextSuffix>€{{ form.controls.kind.value === 'Fixed' ? ('finance.expenses.perMonth' | transloco) : '' }}</span>
      </mat-form-field>
      @if (form.controls.kind.value === 'Fixed') {
        <div class="row">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'finance.expenses.from' | transloco }}</mat-label>
            <input matInput type="month" formControlName="month" />
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'finance.expenses.until' | transloco }}</mat-label>
            <input matInput type="month" formControlName="endMonth" />
            <mat-hint>{{ 'finance.expenses.untilHint' | transloco }}</mat-hint>
          </mat-form-field>
        </div>
      } @else {
        <mat-form-field appearance="outline">
          <mat-label>{{ 'finance.expenses.date' | transloco }}</mat-label>
          <input matInput type="date" formControlName="date" />
        </mat-form-field>
      }
      @if (data.locations.length > 1) {
        <mat-form-field appearance="outline">
          <mat-label>{{ 'finance.location' | transloco }}</mat-label>
          <mat-select formControlName="locationId">
            <mat-option [value]="null">{{ 'finance.allLocations' | transloco }}</mat-option>
            @for (l of data.locations; track l.id) {
              <mat-option [value]="l.id">{{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>{{ 'common.cancel' | transloco }}</button>
      <button mat-flat-button (click)="save()" [disabled]="form.invalid || busy()">{{ 'common.save' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content { display: flex; flex-direction: column; }
    .kind { align-self: flex-start; margin-bottom: var(--space-2); }
    .hint { margin: 0 0 var(--space-4); font: var(--mat-sys-body-small); }
    .row { display: flex; gap: var(--space-3); flex-wrap: wrap; }
    .row mat-form-field { flex: 1; min-width: 160px; }
  `,
})
export class ExpenseDialog {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly ref = inject(MatDialogRef<ExpenseDialog, boolean>);
  protected readonly data = inject<ExpenseDialogData>(MAT_DIALOG_DATA);
  protected readonly busy = signal(false);

  private readonly e = this.data.expense;
  protected readonly form = new FormGroup({
    kind: new FormControl<Kind>(this.e?.kind ?? 'Fixed', { nonNullable: true }),
    name: new FormControl(this.e?.name ?? '', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    amount: new FormControl<number | null>(this.e?.amount ?? null, [Validators.required, Validators.min(0.01)]),
    date: new FormControl((this.e?.date ?? this.data.defaultDate), { nonNullable: true, validators: Validators.required }),
    month: new FormControl((this.e?.date ?? this.data.defaultDate).slice(0, 7), { nonNullable: true, validators: Validators.required }),
    endMonth: new FormControl(this.e?.endsOn?.slice(0, 7) ?? '', { nonNullable: true }),
    locationId: new FormControl<string | null>(this.e?.locationId ?? null),
  });

  protected async save(): Promise<void> {
    const v = this.form.getRawValue();
    const fixed = v.kind === 'Fixed';
    const body = {
      kind: v.kind,
      name: v.name.trim(),
      amount: Number(v.amount),
      locationId: v.locationId,
      date: fixed ? `${v.month}-01` : v.date,
      endsOn: fixed && v.endMonth ? `${v.endMonth}-01` : null,
    };
    this.busy.set(true);
    const { error } = this.e
      ? await this.api.PUT('/api/finance/expenses/{id}', { params: { path: { id: this.e.id } }, body })
      : await this.api.POST('/api/finance/expenses', { body });
    this.busy.set(false);
    if (error) {
      this.notifier.error(error);
      return;
    }
    this.notifier.saved();
    this.ref.close(true);
  }
}
