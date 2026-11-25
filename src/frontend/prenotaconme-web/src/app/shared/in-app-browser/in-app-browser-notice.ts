import { Clipboard } from '@angular/cdk/clipboard';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { isInAppBrowser } from '../../core/auth/in-app-browser';

/**
 * Shown above "Continue with Google" when the page was opened inside an app (e.g. a link from the Telegram bot):
 * Google sign-in does not work there, so explain how to open it in the browser and offer to copy the link.
 */
@Component({
  selector: 'app-in-app-browser-notice',
  imports: [MatButtonModule, MatIconModule, TranslocoPipe],
  template: `
    @if (show) {
      <section class="notice" role="note">
        <mat-icon aria-hidden="true">open_in_browser</mat-icon>
        <div class="text">
          <strong>{{ 'inAppBrowser.title' | transloco }}</strong>
          <span>{{ 'inAppBrowser.hint' | transloco }}</span>
          <button mat-stroked-button type="button" (click)="copy()">
            <mat-icon>{{ copied() ? 'check' : 'content_copy' }}</mat-icon>
            {{ (copied() ? 'inAppBrowser.copied' : 'inAppBrowser.copy') | transloco }}
          </button>
        </div>
      </section>
    }
  `,
  styles: `
    .notice {
      display: flex;
      gap: var(--space-3);
      margin: 0 0 var(--space-4);
      padding: var(--space-3) var(--space-4);
      border-radius: var(--radius-l);
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
      text-align: left;
    }
    mat-icon { flex: none; }
    .text { display: flex; flex-direction: column; gap: var(--space-1); align-items: flex-start; }
    .text span { font: var(--mat-sys-body-medium); }
    button { margin-top: var(--space-2); }
  `,
})
export class InAppBrowserNotice {
  private readonly clipboard = inject(Clipboard);

  protected readonly show = isInAppBrowser();
  protected readonly copied = signal(false);

  protected copy(): void {
    this.copied.set(this.clipboard.copy(globalThis.location?.href ?? ''));
  }
}
