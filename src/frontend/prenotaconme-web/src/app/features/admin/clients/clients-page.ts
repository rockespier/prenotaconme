import { PricePipe } from '../../../shared/price.pipe';
import { Component, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { shortDate } from './client-format';

type Client = Schemas['ClientSummaryDto'];

/** F1-14: the business's clients (registered and entered in the calendar), with search. */
@Component({
  selector: 'app-clients-page',
  imports: [PricePipe, ReactiveFormsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatTableModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.clients' | transloco }}</h1>
    </header>

    <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
      <mat-icon matPrefix>search</mat-icon>
      <mat-label>{{ 'clients.search' | transloco }}</mat-label>
      <input matInput [formControl]="search" autocomplete="off" />
    </mat-form-field>

    <div class="table-card">
      <table mat-table [dataSource]="clients()">
        <ng-container matColumnDef="name">
          <th mat-header-cell *matHeaderCellDef>{{ 'common.name' | transloco }}</th>
          <td mat-cell *matCellDef="let c">
            <strong>{{ c.name || ('clients.noName' | transloco) }}</strong>
            @if (c.isGuest) { <span class="pill">{{ 'calendar.guest' | transloco }}</span> }
            <span class="contact muted">{{ contact(c) }}</span>
          </td>
        </ng-container>
        <ng-container matColumnDef="visits">
          <th mat-header-cell *matHeaderCellDef>{{ 'clients.visits' | transloco }}</th>
          <td mat-cell *matCellDef="let c">{{ c.visits }}</td>
        </ng-container>
        <ng-container matColumnDef="last">
          <th mat-header-cell *matHeaderCellDef class="hide-sm">{{ 'clients.lastVisit' | transloco }}</th>
          <td mat-cell *matCellDef="let c" class="hide-sm">{{ date(c.lastVisit) }}</td>
        </ng-container>
        <ng-container matColumnDef="next">
          <th mat-header-cell *matHeaderCellDef>{{ 'clients.next' | transloco }}</th>
          <td mat-cell *matCellDef="let c">{{ date(c.nextAppointment) }}</td>
        </ng-container>
        <ng-container matColumnDef="spent">
          <th mat-header-cell *matHeaderCellDef class="hide-sm">{{ 'clients.spent' | transloco }}</th>
          <td mat-cell *matCellDef="let c" class="hide-sm">{{ c.spent | price }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="columns"></tr>
        <tr mat-row *matRowDef="let row; columns: columns" class="clickable" tabindex="0"
            (click)="open(row)" (keydown.enter)="open(row)"></tr>
        <tr class="mat-mdc-row no-data" *matNoDataRow>
          <td class="mat-mdc-cell" [attr.colspan]="columns.length">
            {{ (loading() ? 'clients.loading' : search.value ? 'clients.noMatch' : 'clients.empty') | transloco }}
          </td>
        </tr>
      </table>
    </div>
  `,
  styles: `
    .search { width: min(100%, 360px); margin-bottom: var(--space-4); }
    strong { font-weight: 600; margin-right: var(--space-2); }
    .contact { display: block; font: var(--mat-sys-body-small); }
    .clickable { cursor: pointer; }
  `,
})
export class ClientsPage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  protected readonly columns = ['name', 'visits', 'last', 'next', 'spent'];
  protected readonly clients = signal<Client[]>([]);
  protected readonly loading = signal(true);
  protected readonly search = new FormControl('', { nonNullable: true });

  constructor() {
    this.search.valueChanges.pipe(debounceTime(250), distinctUntilChanged()).subscribe(() => void this.load());
  }

  ngOnInit(): void {
    void this.load();
  }

  protected date = (iso: string | null | undefined) => shortDate(iso, this.lang());
  protected contact = (c: Client) => [c.phone, c.email].filter(Boolean).join(' · ');

  protected open(c: Client): void {
    void this.router.navigate(['/admin/clienti/scheda'], { queryParams: { key: c.key } });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    const { data } = await this.api.GET('/api/scheduling/clients', {
      params: { query: { search: this.search.value || undefined } },
    });
    this.clients.set(data ?? []);
    this.loading.set(false);
  }
}
