import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';
import { API_CLIENT } from '../../core/api/api-client';
import { AuthService } from '../../core/auth/auth.service';
import { SessionService } from '../../core/session/session.service';
import { LanguageSwitcher } from '../../shared/language-switcher/language-switcher';
import { environment } from '../../../environments/environment';

interface NavItem {
  path: string;
  icon: string;
  label: string;
}

@Component({
  selector: 'app-backoffice-layout',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    TranslocoPipe,
    LanguageSwitcher,
  ],
  template: `
    <mat-sidenav-container class="container">
      <mat-sidenav #drawer [mode]="isHandset() ? 'over' : 'side'" [opened]="!isHandset()">
        <div class="nav-brand"><span class="mark" aria-hidden="true"></span>{{ 'app.name' | transloco }}</div>
        <mat-nav-list>
          @for (item of nav(); track item.path) {
            <a
              mat-list-item
              [routerLink]="item.path"
              routerLinkActive
              [routerLinkActiveOptions]="{ exact: item.path === '/admin' }"
              #rla="routerLinkActive"
              [activated]="rla.isActive"
              (click)="isHandset() && drawer.close()"
            >
              <mat-icon matListItemIcon>{{ item.icon }}</mat-icon>
              <span matListItemTitle>{{ item.label | transloco }}</span>
            </a>
          }
        </mat-nav-list>
      </mat-sidenav>
      <mat-sidenav-content>
        <mat-toolbar>
          @if (isHandset()) {
            <button mat-icon-button (click)="drawer.toggle()" [attr.aria-label]="'common.menu' | transloco">
              <mat-icon>menu</mat-icon>
            </button>
          }

          <button mat-button [matMenuTriggerFor]="businessMenu" class="business">
            {{ session.currentBusiness()?.name ?? ('app.name' | transloco) }}
            <mat-icon iconPositionEnd>expand_more</mat-icon>
          </button>
          <mat-menu #businessMenu="matMenu">
            @for (b of session.businesses(); track b.slug) {
              <button mat-menu-item (click)="switchBusiness(b.slug)" [disabled]="b.slug === session.currentBusiness()?.slug">
                {{ b.name }}
              </button>
            }
            <mat-divider />
            <a mat-menu-item routerLink="/registra-attivita">
              <mat-icon>add_business</mat-icon>
              {{ 'backoffice.newBusiness' | transloco }}
            </a>
          </mat-menu>

          <span class="spacer"></span>
          <app-language-switcher />

          <button mat-icon-button [matMenuTriggerFor]="userMenu" [attr.aria-label]="'backoffice.account' | transloco">
            @if (auth.user()?.photoURL; as photo) {
              <img class="avatar" [src]="photo" alt="" referrerpolicy="no-referrer" />
            } @else {
              <mat-icon>account_circle</mat-icon>
            }
          </button>
          <mat-menu #userMenu="matMenu">
            <div class="user" mat-menu-item disabled>{{ auth.user()?.displayName ?? auth.user()?.email }}</div>
            <mat-divider />
            @if (session.me()?.isPlatformAdmin) {
              <button mat-menu-item (click)="openJobs()">
                <mat-icon>work_history</mat-icon>
                {{ 'backoffice.jobs' | transloco }}
              </button>
            }
            <button mat-menu-item (click)="signOut()">
              <mat-icon>logout</mat-icon>
              {{ 'backoffice.signOut' | transloco }}
            </button>
          </mat-menu>
        </mat-toolbar>
        <main>
          <router-outlet />
        </main>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .container { height: 100%; }

    mat-sidenav {
      width: 248px;
      border-right: 1px solid var(--mat-sys-outline-variant);
      --mat-sidenav-container-background-color: var(--mat-sys-surface-container-low);
      --mat-sidenav-container-shape: 0;
    }

    .nav-brand {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      height: 64px;
      padding: 0 var(--space-5);
      font: var(--mat-sys-title-small);
      font-weight: 700;
      color: var(--mat-sys-on-surface);
    }

    .mark { width: 12px; height: 12px; border-radius: 4px; background: var(--mat-sys-primary); }

    mat-nav-list {
      padding: 0 var(--space-3);
      --mat-list-active-indicator-shape: var(--radius-m);
      --mat-list-active-indicator-color: var(--mat-sys-primary-container);
    }

    mat-nav-list a[mat-list-item] { margin-bottom: 2px; }

    mat-toolbar {
      position: sticky;
      top: 0;
      z-index: 2;
      --mat-toolbar-container-background-color: var(--mat-sys-surface);
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      gap: var(--space-2);
    }

    .spacer { flex: 1; }
    .business { font: var(--mat-sys-title-medium); font-weight: 600; }
    .avatar { width: 32px; height: 32px; border-radius: 50%; }

    main {
      max-width: 1120px;
      padding: var(--space-6) var(--space-6) var(--space-7);
    }

    @media (max-width: 599px) {
      main { padding: var(--space-5) var(--space-4) var(--space-7); }
    }
  `,
})
export class BackofficeLayout {
  protected readonly auth = inject(AuthService);
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly api = inject(API_CLIENT);

  protected readonly isHandset = toSignal(
    inject(BreakpointObserver)
      .observe(Breakpoints.Handset)
      .pipe(map((r) => r.matches)),
    { initialValue: false },
  );

  protected readonly nav = computed<NavItem[]>(() => [
    { path: '/admin', icon: 'dashboard', label: 'nav.dashboard' },
    { path: '/admin/calendario', icon: 'calendar_month', label: 'nav.calendar' },
    ...(this.session.isAdmin()
      ? [
          { path: '/admin/sedi', icon: 'storefront', label: 'nav.locations' },
          { path: '/admin/servizi', icon: 'content_cut', label: 'nav.services' },
          { path: '/admin/staff', icon: 'groups', label: 'nav.staff' },
          { path: '/admin/clienti', icon: 'contacts', label: 'nav.clients' },
          { path: '/admin/finanze', icon: 'payments', label: 'nav.finance' },
          { path: '/admin/avvisi', icon: 'campaign', label: 'nav.announcements' },
          { path: '/admin/conversazioni', icon: 'support_agent', label: 'nav.handoffs' },
        ]
      : []),
  ]);

  /** Back to the dashboard so no page keeps showing the previous business's data. */
  protected async switchBusiness(slug: string): Promise<void> {
    this.session.selectBusiness(slug);
    await this.router.navigateByUrl('/admin');
  }

  /**
   * F2-01: Hangfire dashboard (platform admins). The API sets a cookie for /hangfire first, because a page
   * navigation cannot carry the bearer token. The tab is opened before the await so popup blockers allow it.
   */
  protected async openJobs(): Promise<void> {
    const tab = window.open('', '_blank');
    const { response } = await this.api.POST('/api/jobs/dashboard-session', { credentials: 'include' });
    if (!tab) return;
    if (response.ok) {
      tab.opener = null;
      tab.location.href = `${environment.apiUrl}/hangfire`;
    } else {
      tab.close();
    }
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/');
  }
}
