import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, TranslocoService } from '@jsverse/transloco';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { API_CLIENT } from '../../core/api/api-client';
import { AVAILABLE_LANGS, Lang } from '../../core/i18n/i18n';
import { SessionService } from '../../core/session/session.service';
import { SLUG_PATTERN, suggestSlug } from './slug';

type SlugState = 'idle' | 'checking' | 'available' | string; // string = error code (slug.taken, slug.reserved, ...)

/** F1-01: a signed-in user registers a business and becomes its Admin. */
@Component({
  selector: 'app-register-business',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, TranslocoPipe],
  template: `
    <mat-card appearance="outlined" class="card">
      <mat-card-header>
        <mat-card-title>{{ 'register.title' | transloco }}</mat-card-title>
        <mat-card-subtitle>{{ 'register.subtitle' | transloco }}</mat-card-subtitle>
      </mat-card-header>

      <form [formGroup]="form" (ngSubmit)="submit()">
        <mat-card-content>
          <mat-form-field>
            <mat-label>{{ 'register.name' | transloco }}</mat-label>
            <input matInput formControlName="name" maxlength="100" required />
          </mat-form-field>

          <mat-form-field>
            <mat-label>{{ 'register.slug' | transloco }}</mat-label>
            <input matInput formControlName="slug" maxlength="50" required (input)="onSlugInput()" />
            <mat-hint>
              @switch (slugState()) {
                @case ('idle') { {{ 'register.slugHint' | transloco }} }
                @case ('checking') { {{ 'register.slugChecking' | transloco }} }
                @case ('available') { ✓ {{ 'register.slugAvailable' | transloco }}: {{ host }}/{{ form.controls.slug.value }} }
                @default { <span class="error">{{ 'errors.' + slugState() | transloco }}</span> }
              }
            </mat-hint>
          </mat-form-field>

          <mat-form-field>
            <mat-label>{{ 'register.timeZone' | transloco }}</mat-label>
            <mat-select formControlName="timeZone">
              @for (tz of timeZones; track tz) {
                <mat-option [value]="tz">{{ tz }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field>
            <mat-label>{{ 'register.language' | transloco }}</mat-label>
            <mat-select formControlName="language">
              @for (lang of langs; track lang) {
                <mat-option [value]="lang">{{ 'languages.' + lang | transloco }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          @if (serverError()) {
            <p class="error" role="alert">{{ 'errors.' + serverError() | transloco }}</p>
          }
        </mat-card-content>

        <mat-card-actions align="end">
          <button mat-flat-button type="submit" [disabled]="form.invalid || busy() || slugState() !== 'available'">
            {{ 'register.submit' | transloco }}
          </button>
        </mat-card-actions>
      </form>
    </mat-card>
  `,
  styles: `
    .card { max-width: 560px; margin: 32px auto; }
    form mat-form-field { display: block; width: 100%; margin-bottom: 8px; }
    .error { color: var(--mat-sys-error); }
  `,
})
export class RegisterBusiness {
  private readonly api = inject(API_CLIENT);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly transloco = inject(TranslocoService);

  protected readonly langs = AVAILABLE_LANGS;
  protected readonly host = globalThis.location?.host ?? '';
  protected readonly timeZones = supportedTimeZones();

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
    slug: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.minLength(3), Validators.maxLength(50), Validators.pattern(SLUG_PATTERN)],
    }),
    timeZone: new FormControl(defaultTimeZone(), { nonNullable: true, validators: Validators.required }),
    language: new FormControl<Lang>(this.transloco.getActiveLang() as Lang, { nonNullable: true }),
  });

  protected readonly slugState = signal<SlugState>('idle');
  protected readonly busy = signal(false);
  protected readonly serverError = signal<string | null>(null);

  private slugEdited = false;

  constructor() {
    // Suggest the slug from the name until the user edits it.
    this.form.controls.name.valueChanges.pipe(takeUntilDestroyed()).subscribe((name) => {
      if (!this.slugEdited) {
        this.form.controls.slug.setValue(suggestSlug(name));
      }
    });

    this.form.controls.slug.valueChanges
      .pipe(debounceTime(350), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((slug) => void this.checkSlug(slug));
  }

  protected onSlugInput(): void {
    this.slugEdited = true;
  }

  private async checkSlug(slug: string): Promise<void> {
    const control = this.form.controls.slug;
    if (!slug) {
      this.slugState.set('idle');
      return;
    }
    if (control.invalid) {
      this.slugState.set(slug.length < 3 || slug.length > 50 ? 'slug.length' : 'slug.format');
      return;
    }

    this.slugState.set('checking');
    const { data } = await this.api.GET('/api/tenancy/slugs/{slug}/availability', { params: { path: { slug } } });
    // Ignore stale answers if the user kept typing.
    if (control.value !== slug) return;
    this.slugState.set(data ? (data.available ? 'available' : (data.reason ?? 'slug.taken')) : 'idle');
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) return;
    this.busy.set(true);
    this.serverError.set(null);

    const { data, response } = await this.api.POST('/api/tenancy/tenants', { body: this.form.getRawValue() });

    if (data) {
      await this.session.load(true);
      this.session.selectBusiness(data.slug);
      await this.router.navigateByUrl('/admin');
    } else {
      this.serverError.set(response.status === 409 ? 'slug.taken' : 'generic');
      if (response.status === 409) this.slugState.set('slug.taken');
    }
    this.busy.set(false);
  }
}

function defaultTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || 'Europe/Rome';
}

/** IANA zones known to the browser; falls back to a short list on older engines. */
function supportedTimeZones(): string[] {
  const intl = Intl as typeof Intl & { supportedValuesOf?: (key: string) => string[] };
  const zones = intl.supportedValuesOf?.('timeZone') ?? ['Europe/Rome', 'Europe/Madrid', 'Europe/London', 'America/Lima'];
  const current = defaultTimeZone();
  return zones.includes(current) ? zones : [current, ...zones];
}
