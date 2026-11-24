import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, inject } from '@angular/core';
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
import { AuthService } from '../../core/auth/auth.service';
import { SessionService } from '../../core/session/session.service';
import { LanguageSwitcher } from '../../shared/language-switcher/language-switcher';

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
        <mat-nav-list>
          @for (item of nav; track item.path) {
            <a
              mat-list-item
              [routerLink]="item.path"
              routerLinkActive
              [routerLinkActiveOptions]="{ exact: true }"
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
              <button mat-menu-item (click)="session.selectBusiness(b.slug)" [disabled]="b.slug === session.currentBusiness()?.slug">
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
    mat-sidenav { width: 240px; }
    .spacer { flex: 1; }
    .business { font: var(--mat-sys-title-medium); }
    .avatar { width: 32px; height: 32px; border-radius: 50%; }
    main { padding: 24px; }
  `,
})
export class BackofficeLayout {
  protected readonly auth = inject(AuthService);
  protected readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly isHandset = toSignal(
    inject(BreakpointObserver)
      .observe(Breakpoints.Handset)
      .pipe(map((r) => r.matches)),
    { initialValue: false },
  );

  protected readonly nav: NavItem[] = [{ path: '/admin', icon: 'dashboard', label: 'nav.dashboard' }];

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/');
  }
}
