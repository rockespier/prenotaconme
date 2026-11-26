import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { API_CLIENT, Schemas } from '../../../core/api/api-client';
import { Notifier } from '../../../shared/api-errors';
import { confirmDelete } from '../../../shared/confirm-dialog';
import { ExpenseDialog, ExpenseDialogData } from './expense-dialog';

type Report = Schemas['FinanceReportDto'];
type Expense = Schemas['ExpenseDto'];

interface Kpi {
  key: string;
  value: number;
  previous: number;
  money: boolean;
  /** Higher is better (income, net); lower is better for expenses. */
  upIsGood: boolean;
}

/**
 * F4-01..06: the month's income, expenses and net profit (vs the previous month), sales by staff member and service,
 * PDF/Excel export, and the list of expenses. Admin only.
 */
@Component({
  selector: 'app-finance-page',
  imports: [MatButtonModule, MatFormFieldModule, MatIconModule, MatSelectModule, MatTabsModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.finance' | transloco }}</h1>
    </header>

    <div class="toolbar">
      <div class="month">
        <button mat-icon-button (click)="shift(-1)" [attr.aria-label]="'finance.previous' | transloco"><mat-icon>chevron_left</mat-icon></button>
        <strong>{{ monthName() }}</strong>
        <button mat-icon-button (click)="shift(1)" [attr.aria-label]="'finance.next' | transloco"><mat-icon>chevron_right</mat-icon></button>
      </div>
      @if (locations().length > 1) {
        <mat-form-field appearance="outline" subscriptSizing="dynamic" class="location">
          <mat-select [value]="locationId()" (valueChange)="locationId.set($event); load()">
            <mat-option [value]="null">{{ 'finance.allLocations' | transloco }}</mat-option>
            @for (l of locations(); track l.id) {
              <mat-option [value]="l.id">{{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      <span class="spacer"></span>
      <button mat-stroked-button (click)="export('pdf')" [disabled]="!report()"><mat-icon>picture_as_pdf</mat-icon>PDF</button>
      <button mat-stroked-button (click)="export('xlsx')" [disabled]="!report()"><mat-icon>table_view</mat-icon>Excel</button>
    </div>

    <mat-tab-group mat-stretch-tabs="false" animationDuration="0ms">
      <mat-tab [label]="'finance.summary' | transloco">
        @if (report(); as r) {
          <div class="kpis">
            @for (k of kpis(); track k.key) {
              <div class="kpi">
                <span class="muted">{{ 'finance.kpi.' + k.key | transloco }}</span>
                <strong [class.negative]="k.key === 'net' && k.value < 0">{{ k.money ? money(k.value) : k.value }}</strong>
                @if (delta(k); as d) {
                  <span class="delta" [class.good]="d.good" [class.bad]="!d.good">
                    <mat-icon inline>{{ d.up ? 'arrow_upward' : 'arrow_downward' }}</mat-icon>{{ d.text }}
                    <span class="muted">{{ 'finance.vsPrevious' | transloco }}</span>
                  </span>
                }
              </div>
            }
          </div>
          @if (r.expectedIncome > 0) {
            <p class="note"><mat-icon inline>schedule</mat-icon>{{ 'finance.expected' | transloco: { amount: money(r.expectedIncome) } }}</p>
          }
          @if (r.unmarked > 0) {
            <p class="note muted"><mat-icon inline>info</mat-icon>{{ 'finance.unmarked' | transloco: { count: r.unmarked } }}</p>
          }

          <h2>{{ 'finance.byDay' | transloco }}</h2>
          <div class="bars" role="img" [attr.aria-label]="'finance.byDay' | transloco">
            @for (d of r.days; track d.date) {
              <div class="bar" [style.height.%]="barHeight(d.income)" [title]="dayLabel(d.date) + ': ' + money(d.income)"></div>
            }
          </div>

          <h2>{{ 'finance.byStaff' | transloco }}</h2>
          <div class="table-card">
            <table>
              <thead><tr>
                <th>{{ 'common.name' | transloco }}</th>
                <th class="num">{{ 'finance.kpi.appointments' | transloco }}</th>
                <th class="num hide-sm">{{ 'finance.noShows' | transloco }}</th>
                <th class="num">{{ 'finance.sales' | transloco }}</th>
              </tr></thead>
              <tbody>
                @for (s of r.staff; track s.staffMemberId) {
                  <tr class="clickable" (click)="toggle(s.staffMemberId)">
                    <td><mat-icon inline>{{ open() === s.staffMemberId ? 'expand_less' : 'expand_more' }}</mat-icon> {{ s.name }}</td>
                    <td class="num">{{ s.appointments }}</td>
                    <td class="num hide-sm">{{ s.noShows }}</td>
                    <td class="num">{{ money(s.sales) }}</td>
                  </tr>
                  @if (open() === s.staffMemberId) {
                    @for (x of s.services; track x.serviceName) {
                      <tr class="sub"><td>{{ x.serviceName }}</td><td class="num">{{ x.count }}</td><td class="hide-sm"></td><td class="num">{{ money(x.sales) }}</td></tr>
                    }
                  }
                } @empty {
                  <tr><td colspan="4" class="empty">{{ 'finance.noAppointments' | transloco }}</td></tr>
                }
              </tbody>
            </table>
          </div>

          <h2>{{ 'finance.byService' | transloco }}</h2>
          <div class="table-card">
            <table>
              <thead><tr>
                <th>{{ 'finance.service' | transloco }}</th>
                <th class="num">{{ 'finance.count' | transloco }}</th>
                <th class="num">{{ 'finance.sales' | transloco }}</th>
              </tr></thead>
              <tbody>
                @for (x of r.services; track x.serviceName) {
                  <tr><td>{{ x.serviceName }}</td><td class="num">{{ x.count }}</td><td class="num">{{ money(x.sales) }}</td></tr>
                } @empty {
                  <tr><td colspan="3" class="empty">{{ 'finance.noAppointments' | transloco }}</td></tr>
                }
              </tbody>
            </table>
          </div>
        }
      </mat-tab>

      <mat-tab [label]="'finance.expenses.title' | transloco">
        <div class="expenses-head">
          <p class="muted">{{ 'finance.expenses.intro' | transloco }}</p>
          <button mat-flat-button (click)="edit(null)"><mat-icon>add</mat-icon>{{ 'finance.expenses.new' | transloco }}</button>
        </div>
        <div class="table-card">
          <table>
            <thead><tr>
              <th>{{ 'finance.expenses.name' | transloco }}</th>
              <th class="hide-sm">{{ 'finance.expenses.when' | transloco }}</th>
              <th class="num">{{ 'finance.expenses.amount' | transloco }}</th>
              <th></th>
            </tr></thead>
            <tbody>
              @for (e of expenses(); track e.id) {
                <tr>
                  <td>
                    {{ e.name }} <span class="pill">{{ 'finance.expenses.' + e.kind | transloco }}</span>
                    @if (locations().length > 1) { <span class="muted small">{{ locationName(e.locationId) }}</span> }
                    <span class="muted small show-sm">{{ when(e) }}</span>
                  </td>
                  <td class="hide-sm">{{ when(e) }}</td>
                  <td class="num">{{ money(e.amount) }}{{ e.kind === 'Fixed' ? ('finance.expenses.perMonth' | transloco) : '' }}</td>
                  <td class="actions">
                    <button mat-icon-button (click)="edit(e)" [attr.aria-label]="'common.edit' | transloco"><mat-icon>edit</mat-icon></button>
                    <button mat-icon-button (click)="remove(e)" [attr.aria-label]="'common.delete' | transloco"><mat-icon>delete</mat-icon></button>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="4" class="empty">{{ 'finance.expenses.empty' | transloco }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      </mat-tab>
    </mat-tab-group>
  `,
  styles: `
    :host { display: block; max-width: 960px; }
    .toolbar { display: flex; align-items: center; flex-wrap: wrap; gap: var(--space-2); margin-bottom: var(--space-4); }
    .month { display: flex; align-items: center; gap: var(--space-1); }
    .month strong { min-width: 150px; text-align: center; font: var(--mat-sys-title-medium); }
    .location { width: 200px; }
    .spacer { flex: 1; }
    .kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: var(--space-3); margin: var(--space-5) 0 var(--space-3); }
    .kpi { display: flex; flex-direction: column; gap: 2px; padding: var(--space-4); border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--radius-l); }
    .kpi strong { font: var(--mat-sys-headline-small); font-weight: 600; font-variant-numeric: tabular-nums; }
    .kpi strong.negative { color: var(--mat-sys-error); }
    .delta { font: var(--mat-sys-body-small); display: inline-flex; align-items: center; gap: 2px; flex-wrap: wrap; }
    .delta.good { color: #1e8e3e; }
    .delta.bad { color: var(--mat-sys-error); }
    .note { display: flex; align-items: center; gap: var(--space-1); margin: var(--space-1) 0; }
    h2 { font: var(--mat-sys-title-medium); font-weight: 600; margin: var(--space-6) 0 var(--space-3); }
    .bars { display: flex; align-items: flex-end; gap: 2px; height: 120px; padding: var(--space-2); border: 1px solid var(--mat-sys-outline-variant); border-radius: var(--radius-l); }
    .bar { flex: 1; min-height: 2px; background: var(--mat-sys-primary); border-radius: 2px 2px 0 0; }
    table { width: 100%; border-collapse: collapse; }
    th, td { padding: var(--space-2) var(--space-3); text-align: left; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    th { font: var(--mat-sys-label-large); color: var(--mat-sys-on-surface-variant); }
    tbody tr:last-child td { border-bottom: 0; }
    .num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .clickable { cursor: pointer; }
    .sub td { padding-left: var(--space-7); color: var(--mat-sys-on-surface-variant); background: var(--mat-sys-surface-container-low); }
    .empty { text-align: center; color: var(--mat-sys-on-surface-variant); padding: var(--space-5); }
    .expenses-head { display: flex; align-items: center; gap: var(--space-3); flex-wrap: wrap; margin: var(--space-4) 0; }
    .expenses-head p { flex: 1; margin: 0; min-width: 220px; }
    .small { display: block; font: var(--mat-sys-body-small); }
    .actions { text-align: right; white-space: nowrap; }
    .show-sm { display: none; }
    @media (max-width: 719px) {
      .show-sm { display: block; }
      th, td { padding: var(--space-2); }
    }
  `,
})
export class FinancePage implements OnInit {
  private readonly api = inject(API_CLIENT);
  private readonly dialog = inject(MatDialog);
  private readonly notifier = inject(Notifier);
  private readonly transloco = inject(TranslocoService);
  private readonly lang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  protected readonly month = signal(currentMonth());
  protected readonly locationId = signal<string | null>(null);
  protected readonly locations = signal<Schemas['LocationDto'][]>([]);
  protected readonly report = signal<Report | null>(null);
  protected readonly expenses = signal<Expense[]>([]);
  protected readonly open = signal<string | null>(null);

  protected readonly monthName = computed(() => {
    const [y, m] = this.month().split('-').map(Number);
    const name = new Intl.DateTimeFormat(this.lang(), { month: 'long', year: 'numeric' }).format(new Date(y, m - 1, 1));
    return name.charAt(0).toUpperCase() + name.slice(1);
  });

  protected readonly kpis = computed<Kpi[]>(() => {
    const r = this.report();
    if (!r) return [];
    const t = r.totals;
    const p = r.previous;
    return [
      { key: 'income', value: t.income, previous: p.income, money: true, upIsGood: true },
      { key: 'expenses', value: t.fixedExpenses + t.variableExpenses, previous: p.fixedExpenses + p.variableExpenses, money: true, upIsGood: false },
      { key: 'net', value: t.net, previous: p.net, money: true, upIsGood: true },
      { key: 'appointments', value: t.appointments, previous: p.appointments, money: false, upIsGood: true },
    ];
  });

  private readonly maxDay = computed(() => Math.max(1, ...(this.report()?.days.map((d) => d.income) ?? [0])));

  ngOnInit(): void {
    void this.loadLocations();
    void this.load();
    void this.loadExpenses();
  }

  protected money = (v: number) => new Intl.NumberFormat(this.lang(), { style: 'currency', currency: 'EUR' }).format(v);

  protected barHeight = (v: number) => (v / this.maxDay()) * 100;

  protected dayLabel = (iso: string) =>
    new Intl.DateTimeFormat(this.lang(), { day: 'numeric', month: 'short' }).format(new Date(`${iso}T12:00:00`));

  protected delta(k: Kpi): { up: boolean; good: boolean; text: string } | null {
    if (k.previous === 0 || k.value === k.previous) return null;
    const change = (k.value - k.previous) / Math.abs(k.previous);
    const up = change > 0;
    return { up, good: up === k.upIsGood, text: new Intl.NumberFormat(this.lang(), { style: 'percent', maximumFractionDigits: 0 }).format(Math.abs(change)) };
  }

  protected when(e: Expense): string {
    const monthYear = (iso: string) =>
      new Intl.DateTimeFormat(this.lang(), { month: 'short', year: 'numeric' }).format(new Date(`${iso}T12:00:00`));
    if (e.kind === 'Variable') {
      return new Intl.DateTimeFormat(this.lang(), { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(`${e.date}T12:00:00`));
    }
    return e.endsOn
      ? `${monthYear(e.date)} – ${monthYear(e.endsOn)}`
      : this.transloco.translate('finance.expenses.since', { month: monthYear(e.date) });
  }

  protected locationName = (id: string | null | undefined) =>
    id ? (this.locations().find((l) => l.id === id)?.name ?? '') : this.transloco.translate('finance.allLocations');

  protected toggle(id: string): void {
    this.open.set(this.open() === id ? null : id);
  }

  protected shift(months: number): void {
    const [y, m] = this.month().split('-').map(Number);
    const d = new Date(y, m - 1 + months, 1);
    this.month.set(`${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`);
    void this.load();
  }

  protected async export(format: 'pdf' | 'xlsx'): Promise<void> {
    const { data, error } = await this.api.GET('/api/finance/report/export', {
      params: { query: { month: this.month(), locationId: this.locationId() ?? undefined, format } },
      parseAs: 'blob',
    });
    if (error || !data) {
      if (error) this.notifier.error(error);
      return;
    }
    const url = URL.createObjectURL(data as Blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${this.month()}.${format}`;
    link.click();
    URL.revokeObjectURL(url);
  }

  protected async edit(expense: Expense | null): Promise<void> {
    const data: ExpenseDialogData = { expense, locations: this.locations(), defaultDate: `${this.month()}-01` };
    const saved = await firstValueFrom(this.dialog.open(ExpenseDialog, { data, width: '520px', maxWidth: '95vw' }).afterClosed());
    if (saved) await Promise.all([this.loadExpenses(), this.load()]);
  }

  protected async remove(e: Expense): Promise<void> {
    if (!(await confirmDelete(this.dialog, this.transloco.translate('finance.expenses.confirmDelete', { name: e.name })))) return;
    const { error } = await this.api.DELETE('/api/finance/expenses/{id}', { params: { path: { id: e.id } } });
    if (error) this.notifier.error(error);
    await Promise.all([this.loadExpenses(), this.load()]);
  }

  protected async load(): Promise<void> {
    const { data } = await this.api.GET('/api/finance/report', {
      params: { query: { month: this.month(), locationId: this.locationId() ?? undefined } },
    });
    this.report.set(data ?? null);
  }

  private async loadExpenses(): Promise<void> {
    const { data } = await this.api.GET('/api/finance/expenses');
    this.expenses.set(data ?? []);
  }

  private async loadLocations(): Promise<void> {
    const { data } = await this.api.GET('/api/locations');
    this.locations.set(data ?? []);
  }
}

function currentMonth(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}
