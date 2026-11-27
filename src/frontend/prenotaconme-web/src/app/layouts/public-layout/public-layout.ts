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
      <a routerLink="/" class="brand"><img src="brand/prenotaconme-logo.svg" [alt]="'app.name' | transloco" width="147" height="24" /></a>
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
    <footer class="site-footer">
      <a routerLink="/privacy">{{ 'privacy.link' | transloco }}</a>
    </footer>
  `,
  styles: `
    :host { display: block; min-height: 100%; }
    mat-toolbar {
      --mat-toolbar-container-background-color: var(--mat-sys-surface);
      --mat-toolbar-standard-height: 56px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .brand { display: inline-flex; align-items: center; }
    .brand img { display: block; height: 24px; width: auto; }
    .spacer { flex: 1; }
    .site-footer { max-width: 960px; margin: var(--space-7) auto 0; padding: var(--space-4); font: var(--mat-sys-body-small); }
    .site-footer a { color: var(--mat-sys-on-surface-variant); }
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
