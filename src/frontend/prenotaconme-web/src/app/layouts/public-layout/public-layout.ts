import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageSwitcher } from '../../shared/language-switcher/language-switcher';

@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, MatButtonModule, TranslocoPipe, LanguageSwitcher],
  template: `
    <mat-toolbar>
      <a routerLink="/" class="brand"><span class="mark" aria-hidden="true"></span>{{ 'app.name' | transloco }}</a>
      <span class="spacer"></span>
      <app-language-switcher />
      @if (business(); as b) {
        <!-- On a business page the client's way in is "my bookings" (sign-in happens there), never the owners' login. -->
        @if (!b.onMyBookings) {
          <a mat-button [routerLink]="['/', b.slug, 'prenotazioni']">{{ 'myAppointments.link' | transloco }}</a>
        }
      } @else if (auth.ready()) {
        @if (auth.isSignedIn()) {
          <a mat-button routerLink="/admin">{{ 'public.backoffice' | transloco }}</a>
        } @else {
          <a mat-button routerLink="/login">{{ 'public.signIn' | transloco }}</a>
        }
      }
    </mat-toolbar>
    <main>
      <router-outlet />
    </main>
  `,
  styles: `
    :host { display: block; min-height: 100%; }
    mat-toolbar {
      --mat-toolbar-container-background-color: var(--mat-sys-surface);
      --mat-toolbar-standard-height: 56px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .brand {
      display: inline-flex; align-items: center; gap: var(--space-2);
      color: var(--mat-sys-on-surface-variant); text-decoration: none;
      font: var(--mat-sys-title-small); font-weight: 600;
    }
    .mark { width: 10px; height: 10px; border-radius: 3px; background: var(--mat-sys-primary); }
    .spacer { flex: 1; }
    main { max-width: 960px; margin: 0 auto; padding: var(--space-5) var(--space-4) 0; }
  `,
})
export class PublicLayout {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Fixed public pages; any other first segment is a business slug (/:slug, /:slug/prenotazioni). */
  private static readonly AppPages = new Set(['', 'login', 'registra-attivita', 'admin', 'b']);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
      startWith(this.router.url),
    ),
    { initialValue: this.router.url },
  );

  protected readonly business = computed(() => {
    const [slug = '', page] = this.url().split(/[?#]/)[0].split('/').filter(Boolean);
    return PublicLayout.AppPages.has(slug) ? null : { slug, onMyBookings: page === 'prenotazioni' };
  });
}
