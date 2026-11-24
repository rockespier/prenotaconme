import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { confirmDelete } from '../../../shared/confirm-dialog';
import { StaffDialog, StaffDialogData } from './staff-dialog';

type Staff = Schemas['StaffDto'];

/** F1-06: staff members, their locations and services. */
@Component({
  selector: 'app-staff-page',
  imports: [RouterLink, MatButtonModule, MatChipsModule, MatIconModule, MatTableModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.staff' | transloco }}</h1>
      <button mat-flat-button (click)="edit(null)"><mat-icon>person_add</mat-icon>{{ 'staff.new' | transloco }}</button>
    </header>
    <p class="muted">{{ 'staff.inviteInfo' | transloco }}</p>

    <table mat-table [dataSource]="staff()">
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>{{ 'common.name' | transloco }}</th>
        <td mat-cell *matCellDef="let s">
          {{ s.displayName }}
          @if (!s.isActive) { <small class="muted">({{ 'staff.inactive' | transloco }})</small> }
        </td>
      </ng-container>
      <ng-container matColumnDef="email">
        <th mat-header-cell *matHeaderCellDef>{{ 'staff.email' | transloco }}</th>
        <td mat-cell *matCellDef="let s">{{ s.email }}</td>
      </ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>{{ 'staff.status' | transloco }}</th>
        <td mat-cell *matCellDef="let s">
          <mat-chip [highlighted]="s.isLinked">{{ (s.isLinked ? 'staff.linked' : 'staff.invited') | transloco }}</mat-chip>
        </td>
      </ng-container>
      <ng-container matColumnDef="locations">
        <th mat-header-cell *matHeaderCellDef>{{ 'nav.locations' | transloco }}</th>
        <td mat-cell *matCellDef="let s">{{ locationNames(s) }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let s" class="actions">
          <a mat-button [routerLink]="['/admin/staff', s.id]"><mat-icon>schedule</mat-icon>{{ 'staff.schedule' | transloco }}</a>
          <button mat-icon-button (click)="edit(s)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button (click)="remove(s)" [attr.aria-label]="'common.delete' | transloco"><mat-icon>delete</mat-icon></button>
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
    </table>
  `,
  styles: `
    .page-header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    table { width: 100%; }
    .actions { text-align: right; white-space: nowrap; }
    .muted { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class StaffPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly columns = ['name', 'email', 'status', 'locations', 'actions'];
  protected readonly staff = signal<Staff[]>([]);
  private readonly locations = signal<Schemas['LocationDto'][]>([]);
  private readonly services = signal<Schemas['ServiceDto'][]>([]);
  private readonly locationById = computed(() => new Map(this.locations().map((l) => [l.id, l.name])));

  ngOnInit(): void {
    void this.load();
  }

  protected locationNames(s: Staff): string {
    return s.locationIds.map((id) => this.locationById().get(id)).filter(Boolean).join(', ');
  }

  protected async edit(staff: Staff | null): Promise<void> {
    const data: StaffDialogData = { staff, locations: this.locations(), services: this.services() };
    const saved = await firstValueFrom(this.dialog.open(StaffDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  protected async remove(staff: Staff): Promise<void> {
    if (!(await confirmDelete(this.dialog, this.transloco.translate('staff.confirmDelete', { name: staff.displayName })))) return;
    const { error } = await this.api.DELETE('/api/staff/{id}', { params: { path: { id: staff.id } } });
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const [staff, locations, services] = await Promise.all([
      this.api.GET('/api/staff'),
      this.api.GET('/api/locations'),
      this.api.GET('/api/catalog/services'),
    ]);
    this.staff.set(staff.data ?? []);
    this.locations.set(locations.data ?? []);
    this.services.set(services.data ?? []);
  }
}
