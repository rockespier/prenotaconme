import { Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login',
  imports: [MatButtonModule, MatCardModule, MatIconModule, TranslocoPipe],
  template: `
    <mat-card appearance="outlined" class="card">
      <mat-card-header>
        <mat-card-title>{{ 'login.title' | transloco }}</mat-card-title>
        <mat-card-subtitle>{{ 'login.subtitle' | transloco }}</mat-card-subtitle>
      </mat-card-header>
      <mat-card-content>
        @if (error()) {
          <p class="error" role="alert">{{ 'login.error' | transloco }}</p>
        }
      </mat-card-content>
      <mat-card-actions>
        <button mat-flat-button (click)="signIn()" [disabled]="busy()">
          <mat-icon>login</mat-icon>
          {{ 'login.google' | transloco }}
        </button>
      </mat-card-actions>
    </mat-card>
  `,
  styles: `
    .card { max-width: 420px; margin: 48px auto; }
    .error { color: var(--mat-sys-error); }
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Bound from ?returnUrl= */
  readonly returnUrl = input<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal(false);

  protected async signIn(): Promise<void> {
    this.busy.set(true);
    this.error.set(false);
    try {
      await this.auth.signInWithGoogle();
      await this.router.navigateByUrl(this.safeReturnUrl());
    } catch {
      this.error.set(true);
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
