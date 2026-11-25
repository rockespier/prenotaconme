import { Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';
import { signInErrorKey } from '../../core/auth/in-app-browser';
import { InAppBrowserNotice } from '../../shared/in-app-browser/in-app-browser-notice';

@Component({
  selector: 'app-login',
  imports: [InAppBrowserNotice, MatButtonModule, MatCardModule, MatIconModule, TranslocoPipe],
  template: `
    <section class="panel">
      <h1>{{ (registering() ? 'login.registerTitle' : 'login.title') | transloco }}</h1>
      <p class="muted">{{ (registering() ? 'login.registerSubtitle' : 'login.subtitle') | transloco }}</p>
      <app-in-app-browser-notice />
      @if (error(); as key) {
        <p class="error" role="alert"><mat-icon>error_outline</mat-icon>{{ key | transloco }}</p>
      }
      <button mat-flat-button class="google" (click)="signIn()" [disabled]="busy()">
        <mat-icon>login</mat-icon>
        {{ 'login.google' | transloco }}
      </button>
    </section>
  `,
  styles: `
    .panel {
      max-width: 400px;
      margin: var(--space-7) auto;
      padding: var(--space-6) var(--space-5);
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: var(--radius-l);
    }
    h1 { margin-bottom: var(--space-2); }
    .muted { margin: 0 0 var(--space-5); }
    .google { width: 100%; height: 48px; }
    .error {
      display: flex; gap: var(--space-2); align-items: flex-start;
      margin: 0 0 var(--space-4); padding: var(--space-3);
      border-radius: var(--radius-m);
      background: var(--mat-sys-error-container); color: var(--mat-sys-on-error-container);
    }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Bound from ?returnUrl= */
  readonly returnUrl = input<string>();

  /** Came from "Registra la tua attività": say why Google sign-in is needed. */
  protected readonly registering = computed(() => this.returnUrl()?.startsWith('/registra-attivita') ?? false);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.signInWithGoogle();
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch {
      this.error.set(signInErrorKey());
    } finally {
      this.busy.set(false);
    }
  }

  /** Only same-app paths, to avoid open redirects. */
  private safeReturnUrl(): string {
    const url = this.returnUrl();
    return url && url.startsWith('/') && !url.startsWith('//') ? url : '/admin';
  }
}
