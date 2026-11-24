import { Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { confirmDelete } from '../../../shared/confirm-dialog';
import { toHm, WEEK_DAYS } from '../../../shared/time';
import { LocationDialog } from './location-dialog';

type Location = Schemas['LocationDto'];

/** F1-04: business locations and opening hours. */
@Component({
  selector: 'app-locations-page',
  imports: [MatButtonModule, MatCardModule, MatIconModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.locations' | transloco }}</h1>
      <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'locations.new' | transloco }}</button>
    </header>

    <div class="grid">
      @for (l of locations(); track l.id) {
        <mat-card appearance="outlined">
          <mat-card-header>
            <mat-card-title>{{ l.name }} @if (!l.isActive) { <small>({{ 'common.inactive' | transloco }})</small> }</mat-card-title>
            <mat-card-subtitle>{{ place(l) }}</mat-card-subtitle>
          </mat-card-header>
          <mat-card-content>
            <dl>
              @for (day of days; track day) {
                <dt>{{ 'days.' + day | transloco }}</dt>
                <dd>{{ summary(l, day) || ('locations.closed' | transloco) }}</dd>
              }
            </dl>
          </mat-card-content>
          <mat-card-actions align="end">
            <button mat-button (click)="remove(l)">{{ 'common.delete' | transloco }}</button>
            <button mat-button (click)="edit(l)">{{ 'common.edit' | transloco }}</button>
          </mat-card-actions>
        </mat-card>
      } @empty {
        <p>{{ 'locations.empty' | transloco }}</p>
      }
    </div>
  `,
  styles: `
    .page-header { display: flex; align-items: center; justify-content: space-between; gap: 16px; }
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }
    dl { display: grid; grid-template-columns: auto 1fr; gap: 2px 16px; margin: 8px 0 0; }
    dt { color: var(--mat-sys-on-surface-variant); }
    dd { margin: 0; }
  `,
})
export class LocationsPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);

  protected readonly days = WEEK_DAYS;
  protected readonly locations = signal<Location[]>([]);

  ngOnInit(): void {
    void this.load();
  }

  protected place(l: Location): string {
    return [l.address, l.city].filter((x) => !!x).join(', ');
  }

  protected summary(l: Location, day: string): string {
    return l.openingHours.filter((h) => h.day === day).map((h) => `${toHm(h.opens)}–${toHm(h.closes)}`).join(', ');
  }

  protected async edit(location: Location | null): Promise<void> {
    const saved = await firstValueFrom(
      this.dialog.open(LocationDialog, { data: location, width: '640px', maxWidth: '95vw' }).afterClosed(),
    );
    if (saved) await this.load();
  }

  protected async remove(location: Location): Promise<void> {
    const message = this.transloco.translate('locations.confirmDelete', { name: location.name });
    if (!(await confirmDelete(this.dialog, message))) return;

    const { error } = await this.api.DELETE('/api/locations/{id}', { params: { path: { id: location.id } } });
    if (error) this.notifier.error(error);
    await this.load();
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/locations');
    this.locations.set(data ?? []);
  }
}
