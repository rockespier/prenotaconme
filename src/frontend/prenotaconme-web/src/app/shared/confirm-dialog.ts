import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { TranslocoPipe } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

interface ConfirmData {
  message: string;
}

@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule, TranslocoPipe],
  template: `
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="false">{{ 'common.cancel' | transloco }}</button>
      <button mat-flat-button [mat-dialog-close]="true">{{ 'common.delete' | transloco }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmData>(MAT_DIALOG_DATA);
}

/** Material confirmation dialog (never the browser's confirm()). */
export function confirmDelete(dialog: MatDialog, message: string): Promise<boolean> {
  return firstValueFrom(dialog.open(ConfirmDialog, { data: { message } }).afterClosed()).then((r) => r === true);
}
