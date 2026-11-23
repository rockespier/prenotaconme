import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { AVAILABLE_LANGS, Lang, saveLang } from '../../core/i18n/i18n';

@Component({
  selector: 'app-language-switcher',
  imports: [MatButtonModule, MatMenuModule, TranslocoPipe],
  template: `
    <button mat-button [matMenuTriggerFor]="menu" [attr.aria-label]="'common.language' | transloco">
      {{ activeLang()?.toUpperCase() }}
    </button>
    <mat-menu #menu="matMenu">
      @for (lang of langs; track lang) {
        <button mat-menu-item (click)="use(lang)" [disabled]="lang === activeLang()">
          {{ 'languages.' + lang | transloco }}
        </button>
      }
    </mat-menu>
  `,
})
export class LanguageSwitcher {
  private readonly transloco = inject(TranslocoService);

  protected readonly langs = AVAILABLE_LANGS;
  protected readonly activeLang = toSignal(this.transloco.langChanges$);

  protected use(lang: Lang): void {
    this.transloco.setActiveLang(lang);
    saveLang(lang);
  }
}
