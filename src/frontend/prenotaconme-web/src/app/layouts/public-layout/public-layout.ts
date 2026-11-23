import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { TranslocoPipe } from '@jsverse/transloco';
import { LanguageSwitcher } from '../../shared/language-switcher/language-switcher';

@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, RouterLink, MatToolbarModule, TranslocoPipe, LanguageSwitcher],
  template: `
    <mat-toolbar>
      <a routerLink="/" class="brand">{{ 'app.name' | transloco }}</a>
      <span class="spacer"></span>
      <app-language-switcher />
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
export class PublicLayout {}
