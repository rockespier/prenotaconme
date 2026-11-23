import { BreakpointObserver, Breakpoints } from '@angular/cdk/layout';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { map } from 'rxjs';
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
    TranslocoPipe,
    LanguageSwitcher,
  ],
  template: `
    <mat-sidenav-container class="container">
      <mat-sidenav #drawer [mode]="isHandset() ? 'over' : 'side'" [opened]="!isHandset()">
        <mat-nav-list>
          @for (item of nav; track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive #rla="routerLinkActive" [activated]="rla.isActive">
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
          <span>{{ 'app.name' | transloco }}</span>
          <span class="spacer"></span>
          <app-language-switcher />
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
    main { padding: 24px; }
  `,
})
export class BackofficeLayout {
  private readonly handset = toSignal(
    inject(BreakpointObserver)
      .observe(Breakpoints.Handset)
      .pipe(map((r) => r.matches)),
    { initialValue: false },
  );

  protected readonly isHandset = computed(() => this.handset());

  protected readonly nav: NavItem[] = [{ path: '/admin', icon: 'dashboard', label: 'nav.dashboard' }];
}
