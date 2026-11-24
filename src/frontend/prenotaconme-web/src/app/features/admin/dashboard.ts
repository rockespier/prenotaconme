import { Clipboard } from '@angular/cdk/clipboard';
import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { API_CLIENT } from '../../core/api/api-client';
import { SessionService } from '../../core/session/session.service';

interface SetupStep {
  key: string;
  done: boolean;
  link: string;
}

/**
 * Overview: the public booking link (copy/open) and, for Admins, what is still missing before clients can book
 * (a location, an active service, an active staff member with working hours).
 */
@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    <header class="page-header">
      <h1>{{ 'nav.dashboard' | transloco }}</h1>
    </header>

    @if (session.currentBusiness(); as business) {
      <section class="link-panel">
        <div class="link-text">
          <h2>{{ 'dashboard.publicLinkTitle' | transloco }}</h2>
          <p class="muted">{{ 'dashboard.publicLink' | transloco }}</p>
          <code class="url">{{ publicUrl() }}</code>
        </div>
        <div class="link-actions">
          <button mat-flat-button (click)="copy()"><mat-icon>content_copy</mat-icon>{{ 'dashboard.copy' | transloco }}</button>
          <a mat-stroked-button [href]="'/' + business.slug" target="_blank" rel="noopener">
            <mat-icon>open_in_new</mat-icon>{{ 'dashboard.open' | transloco }}
          </a>
        </div>
      </section>

      @if (session.isAdmin() && steps(); as list) {
        <section class="setup">
          <h2>
            @if (allDone()) {
              {{ 'dashboard.readyTitle' | transloco }}
            } @else {
              {{ 'dashboard.setupTitle' | transloco }}
            }
          </h2>
          <p class="muted">
            {{ (allDone() ? 'dashboard.readyHint' : 'dashboard.setupHint') | transloco }}
          </p>
          <ol class="steps">
            @for (s of list; track s.key) {
              <li [class.done]="s.done">
                <span class="check" aria-hidden="true">
                  <mat-icon>{{ s.done ? 'check' : 'radio_button_unchecked' }}</mat-icon>
                </span>
                <span class="step-text">
                  <strong>{{ 'dashboard.steps.' + s.key + '.title' | transloco }}</strong>
                  <span class="muted">{{ 'dashboard.steps.' + s.key + '.hint' | transloco }}</span>
                </span>
                @if (!s.done) {
                  <a mat-button [routerLink]="s.link">{{ 'dashboard.go' | transloco }}<mat-icon iconPositionEnd>arrow_forward</mat-icon></a>
                }
              </li>
            }
          </ol>
        </section>
      }
    }
  `,
  styles: `
    :host { display: block; max-width: 880px; }

    h2 { margin: 0 0 var(--space-1); }

    .link-panel {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      justify-content: space-between;
      gap: var(--space-4);
      padding: var(--space-5);
      border-radius: var(--radius-l);
      background: var(--mat-sys-primary-container);
      color: var(--mat-sys-on-primary-container);

      .muted { color: inherit; opacity: 0.85; margin: 0 0 var(--space-3); }
    }

    .link-text { min-width: 0; }

    .url {
      display: block;
      font: var(--mat-sys-title-medium);
      font-weight: 600;
      overflow-wrap: anywhere;
    }

    .link-actions { display: flex; flex-wrap: wrap; gap: var(--space-2); }

    .setup { margin-top: var(--space-6); }

    .steps {
      list-style: none;
      margin: var(--space-4) 0 0;
      padding: 0;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);

      li {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        min-height: 64px;
        padding: var(--space-3) var(--space-4);
        border-top: 1px solid var(--mat-sys-outline-variant);

        &:first-child { border-top: 0; }
      }

      .done .step-text strong { color: var(--mat-sys-on-surface-variant); }
    }

    .check {
      display: grid;
      place-items: center;
      width: 32px;
      height: 32px;
      flex: 0 0 auto;
      border-radius: 50%;
      color: var(--mat-sys-outline);

      .done & {
        background: var(--mat-sys-tertiary-container);
        color: var(--mat-sys-on-tertiary-container);
      }
    }

    .step-text { flex: 1; display: grid; gap: 2px; min-width: 0; }
    .step-text .muted { font: var(--mat-sys-body-medium); }
  `,
})
export class Dashboard {
  private readonly api = inject(API_CLIENT);
  private readonly clipboard = inject(Clipboard);
  private readonly snackBar = inject(MatSnackBar);
  private readonly transloco = inject(TranslocoService);
  protected readonly session = inject(SessionService);

  protected readonly publicUrl = computed(() => `${globalThis.location?.origin ?? ''}/${this.session.currentBusiness()?.slug ?? ''}`);
  protected readonly steps = signal<SetupStep[] | null>(null);
  protected readonly allDone = computed(() => this.steps()?.every((s) => s.done) ?? false);

  constructor() {
    // Reload when the selected business changes.
    effect(() => {
      const business = this.session.currentBusiness();
      const admin = this.session.isAdmin();
      untracked(() => {
        this.steps.set(null);
        if (business && admin) void this.loadSetup();
      });
    });
  }

  protected copy(): void {
    this.clipboard.copy(this.publicUrl());
    this.snackBar.open(this.transloco.translate('dashboard.copied'), undefined, { duration: 2000 });
  }

  private async loadSetup(): Promise<void> {
    const [locations, services, staff] = await Promise.all([
      this.api.GET('/api/locations'),
      this.api.GET('/api/catalog/services'),
      this.api.GET('/api/staff'),
    ]);

    const activeStaff = (staff.data ?? []).filter((s) => s.isActive);
    const schedules = await Promise.all(
      activeStaff.map((s) => this.api.GET('/api/staff/{id}/schedule', { params: { path: { id: s.id } } })),
    );

    this.steps.set([
      { key: 'location', done: (locations.data ?? []).some((l) => l.isActive && l.openingHours.length > 0), link: '/admin/sedi' },
      { key: 'services', done: (services.data ?? []).some((s) => s.isActive), link: '/admin/servizi' },
      { key: 'staff', done: activeStaff.length > 0, link: '/admin/staff' },
      { key: 'hours', done: schedules.some((r) => (r.data?.workingHours.length ?? 0) > 0), link: '/admin/staff' },
    ]);
  }
}
