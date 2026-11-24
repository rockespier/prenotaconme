import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
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
      <a routerLink="/" class="brand">{{ 'app.name' | transloco }}</a>
      <span class="spacer"></span>
      <app-language-switcher />
      @if (auth.ready()) {
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
    .brand { color: inherit; text-decoration: none; font: var(--mat-sys-title-large); }
    .spacer { flex: 1; }
    main { max-width: 960px; margin: 0 auto; padding: 24px 16px; }
  `,
})
export class PublicLayout {
  protected readonly auth = inject(AuthService);
}
