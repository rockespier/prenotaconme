import { PricePipe } from '../../../shared/price.pipe';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { confirmDelete } from '../../../shared/confirm-dialog';
import { ServiceDialog, ServiceDialogData } from './service-dialog';

type Service = Schemas['ServiceDto'];

/** F1-05: service catalog (duration, price, locations). */
@Component({
  selector: 'app-services-page',
  imports: [PricePipe, MatButtonModule, MatIconModule, MatTableModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.services' | transloco }}</h1>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'services.new' | transloco }}</button>
    </header>

    <div class="table-card">
    <table mat-table [dataSource]="services()">
      <ng-container matColumnDef="name">
        <th mat-header-cell *matHeaderCellDef>{{ 'common.name' | transloco }}</th>
        <td mat-cell *matCellDef="let s">
          <strong>{{ s.name }}</strong>
          @if (!s.isActive) { <span class="pill">{{ 'services.inactive' | transloco }}</span> }
        </td>
      </ng-container>
      <ng-container matColumnDef="duration">
        <th mat-header-cell *matHeaderCellDef>{{ 'services.duration' | transloco }}</th>
        <td mat-cell *matCellDef="let s">{{ s.durationMinutes }} min</td>
      </ng-container>
      <ng-container matColumnDef="price">
        <th mat-header-cell *matHeaderCellDef>{{ 'services.price' | transloco }}</th>
        <td mat-cell *matCellDef="let s">{{ s.price | price }}</td>
      </ng-container>
      <ng-container matColumnDef="locations">
        <th mat-header-cell *matHeaderCellDef class="hide-sm">{{ 'nav.locations' | transloco }}</th>
        <td mat-cell *matCellDef="let s" class="hide-sm">{{ locationNames(s) }}</td>
      </ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let s" class="actions">
          <button mat-icon-button (click)="edit(s)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
          <button mat-icon-button (click)="remove(s)" [attr.aria-label]="'common.delete' | transloco"><mat-icon>delete</mat-icon></button>
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let row; columns: columns"></tr>
      <tr class="mat-mdc-row no-data" *matNoDataRow>
        <td class="mat-mdc-cell" [attr.colspan]="columns.length">{{ 'services.empty' | transloco }}</td>
      </tr>
    </table>
    </div>
  `,
  styles: `
    strong { font-weight: 600; margin-right: var(--space-2); }
  `,
})
export class ServicesPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly columns = ['name', 'duration', 'price', 'locations', 'actions'];
  protected readonly services = signal<Service[]>([]);
  private readonly locations = signal<Schemas['LocationDto'][]>([]);
  private readonly locationById = computed(() => new Map(this.locations().map((l) => [l.id, l.name])));

  ngOnInit(): void {
    void this.load();
  }

  protected locationNames(s: Service): string {
    return s.locationIds.map((id) => this.locationById().get(id)).filter(Boolean).join(', ');
  }

  protected async edit(service: Service | null): Promise<void> {
    const data: ServiceDialogData = { service, locations: this.locations() };
    const saved = await firstValueFrom(this.dialog.open(ServiceDialog, { data, width: '560px', maxWidth: '95vw' }).afterClosed());
    if (saved) await this.load();
  }

  protected async remove(service: Service): Promise<void> {
    if (!(await confirmDelete(this.dialog, this.transloco.translate('services.confirmDelete', { name: service.name })))) return;
    const { error } = await this.api.DELETE('/api/catalog/services/{id}', { params: { path: { id: service.id } } });
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const [services, locations] = await Promise.all([this.api.GET('/api/catalog/services'), this.api.GET('/api/locations')]);
    this.services.set(services.data ?? []);
    this.locations.set(locations.data ?? []);
  }
}
