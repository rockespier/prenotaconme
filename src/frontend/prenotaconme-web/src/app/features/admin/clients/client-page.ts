import { PricePipe } from '../../../shared/price.pipe';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { appointmentDate, shortDate } from './client-format';

/** F1-14: one client's contact details, numbers and appointment history. Route /admin/clienti/scheda?key=... */
@Component({
  selector: 'app-client-page',
  imports: [PricePipe, RouterLink, MatButtonModule, MatIconModule, MatTableModule, TranslocoPipe],
  template: `
    <a mat-button routerLink="/admin/clienti" class="back"><mat-icon>arrow_back</mat-icon>{{ 'nav.clients' | transloco }}</a>

    @if (state() === 'notFound') {
      <p class="muted">{{ 'clients.notFound' | transloco }}</p>
    } @else if (detail(); as d) {
      <header class="page-header">
        <h1>
          {{ d.client.name || ('clients.noName' | transloco) }}
          @if (d.client.isGuest) { <span class="pill">{{ 'calendar.guest' | transloco }}</span> }
        </h1>
        <p class="contact">
          @if (d.client.phone) {
            <a [href]="'tel:' + d.client.phone"><mat-icon inline>call</mat-icon>{{ d.client.phone }}</a>
          }
          @if (d.client.email) {
            <a [href]="'mailto:' + d.client.email"><mat-icon inline>mail</mat-icon>{{ d.client.email }}</a>
          }
        </p>
      </header>

      <dl class="stats">
        <div><dt>{{ 'clients.visits' | transloco }}</dt><dd>{{ d.client.visits }}</dd></div>
        <div><dt>{{ 'clients.spent' | transloco }}</dt><dd>{{ d.client.spent | price }}</dd></div>
        <div><dt>{{ 'clients.lastVisit' | transloco }}</dt><dd>{{ date(d.client.lastVisit) }}</dd></div>
        <div><dt>{{ 'clients.next' | transloco }}</dt><dd>{{ date(d.client.nextAppointment) }}</dd></div>
        <div><dt>{{ 'clients.cancelled' | transloco }}</dt><dd>{{ d.client.cancelled }}</dd></div>
      </dl>

      <h2>{{ 'clients.history' | transloco }}</h2>
      <div class="table-card">
        <table mat-table [dataSource]="d.appointments">
          <ng-container matColumnDef="when">
            <th mat-header-cell *matHeaderCellDef>{{ 'calendar.date' | transloco }}</th>
            <td mat-cell *matCellDef="let a" [class.struck]="a.status === 'Cancelled'">{{ when(a) }}</td>
          </ng-container>
          <ng-container matColumnDef="service">
            <th mat-header-cell *matHeaderCellDef>{{ 'calendar.service' | transloco }}</th>
            <td mat-cell *matCellDef="let a">
              {{ a.serviceName }}
              <span class="muted small">{{ a.staffName }}@if (multiLocation()) { · {{ a.locationName }} }</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="price">
            <th mat-header-cell *matHeaderCellDef class="hide-sm">{{ 'services.price' | transloco }}</th>
            <td mat-cell *matCellDef="let a" class="hide-sm">{{ a.price | price }}</td>
          </ng-container>
          <ng-container matColumnDef="status">
            <th mat-header-cell *matHeaderCellDef></th>
            <td mat-cell *matCellDef="let a" class="actions">
              <span class="pill" [class.ok]="a.status === 'Confirmed' && isFuture(a)">{{ status(a) | transloco }}</span>
            </td>
          </ng-container>
          <tr mat-header-row *matHeaderRowDef="columns"></tr>
          <tr mat-row *matRowDef="let row; columns: columns"></tr>
        </table>
      </div>
    }
  `,
  styles: `
    .back { margin: 0 0 var(--space-3) calc(-1 * var(--space-3)); }
    h1 .pill { vertical-align: middle; margin-left: var(--space-2); }
    .contact { display: flex; flex-wrap: wrap; gap: var(--space-4); }
    .contact a { display: inline-flex; align-items: center; gap: var(--space-1); color: inherit; }
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
      gap: var(--space-3);
      margin: 0 0 var(--space-6);
    }
    .stats div {
      padding: var(--space-3) var(--space-4);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    .stats dt { font: var(--mat-sys-label-medium); color: var(--mat-sys-on-surface-variant); }
    .stats dd { margin: var(--space-1) 0 0; font: var(--mat-sys-title-medium); font-weight: 600; }
    .small { display: block; font: var(--mat-sys-body-small); }
    .struck { text-decoration: line-through; color: var(--mat-sys-on-surface-variant); }
    td::first-letter { text-transform: uppercase; }
  `,
})
export class ClientPage {
  private readonly api = inject(API_CLIENT);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  /** Query param ?key= */
  readonly key = input<string>('');

  protected readonly columns = ['when', 'service', 'price', 'status'];
  protected readonly state = signal<'loading' | 'ready' | 'notFound'>('loading');
  protected readonly detail = signal<Schemas['ClientDetailDto'] | null>(null);
  protected readonly multiLocation = computed(
    () => new Set(this.detail()?.appointments.map((a) => a.locationName) ?? []).size > 1,
  );

  constructor() {
    effect(() => {
      const key = this.key();
      untracked(() => void this.load(key));
    });
  }

  protected date = (iso: string | null | undefined) => shortDate(iso, this.lang());
  protected when = (a: Schemas['ClientAppointmentDto']) => appointmentDate(a.startsAt, a.timeZone, this.lang());
  protected isFuture = (a: Schemas['ClientAppointmentDto']) => new Date(a.startsAt).getTime() > Date.now();

  /** Confirmed and already started reads as "done" (no-shows are not tracked yet). */
  protected status(a: Schemas['ClientAppointmentDto']): string {
    if (a.status === 'Confirmed') return this.isFuture(a) ? 'clients.status.upcoming' : 'clients.status.done';
    return `myAppointments.status.${a.status}`;
  }

  private async load(key: string): Promise<void> {
    if (!key) {
      this.state.set('notFound');
      return;
    }
    this.state.set('loading');
    const { data } = await this.api.GET('/api/scheduling/clients/detail', { params: { query: { key } } });
    this.detail.set(data ?? null);
    this.state.set(data ? 'ready' : 'notFound');
  }
}
