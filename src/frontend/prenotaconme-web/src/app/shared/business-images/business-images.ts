import { Component, effect, inject, signal, untracked } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, assetUrl } from '../../core/api/api-client';
import { SessionService } from '../../core/session/session.service';
import { Notifier } from '../api-errors';
import { BusinessImageKind, resizeImage } from './resize-image';

/** F5-08: the Admin sets the logo and the cover image of the booking page (dashboard). */
@Component({
  selector: 'app-business-images',
  imports: [MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoPipe],
  template: `
    <section class="images">
      <h2>{{ 'businessImages.title' | transloco }}</h2>
      <p class="muted">{{ 'businessImages.hint' | transloco }}</p>

      <div class="slots">
        @for (kind of kinds; track kind) {
          <div class="slot" [class]="kind">
            <div class="preview">
              @if (busy() === kind) {
                <mat-spinner diameter="28" />
              } @else if (urls()[kind]; as url) {
                <img [src]="url" [alt]="'businessImages.' + kind | transloco" />
              } @else {
                <mat-icon aria-hidden="true">{{ kind === 'logo' ? 'storefront' : 'panorama' }}</mat-icon>
              }
            </div>
            <div class="text">
              <strong>{{ 'businessImages.' + kind | transloco }}</strong>
              <span class="muted">{{ 'businessImages.' + kind + 'Hint' | transloco }}</span>
              <div class="actions">
                <label mat-stroked-button class="upload" [class.disabled]="busy()">
                  <mat-icon>upload</mat-icon>{{ (urls()[kind] ? 'businessImages.change' : 'businessImages.upload') | transloco }}
                  <input type="file" accept="image/png,image/jpeg,image/webp" [disabled]="busy() !== null" (change)="upload(kind, $event)" />
                </label>
                @if (urls()[kind]) {
                  <button mat-button (click)="remove(kind)" [disabled]="busy() !== null">{{ 'businessImages.remove' | transloco }}</button>
                }
              </div>
            </div>
          </div>
        }
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    h2 { margin: 0 0 var(--space-1); }
    .muted { margin: 0 0 var(--space-4); }
    .slots { display: grid; gap: var(--space-4); }
    .slot { display: flex; gap: var(--space-4); align-items: center; flex-wrap: wrap; }
    .preview {
      flex: 0 0 auto;
      display: grid; place-items: center;
      overflow: hidden;
      background: var(--mat-sys-surface-container);
      color: var(--mat-sys-on-surface-variant);
      border: 1px solid var(--mat-sys-outline-variant);
    }
    .preview img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .logo .preview { width: 72px; height: 72px; border-radius: 50%; }
    .cover .preview { width: 192px; height: 72px; border-radius: var(--radius-m); }
    .text { display: flex; flex-direction: column; gap: var(--space-1); flex: 1; min-width: 200px; }
    .text .muted { margin: 0; font: var(--mat-sys-body-small); }
    .actions { display: flex; gap: var(--space-2); flex-wrap: wrap; margin-top: var(--space-1); }
    .upload { position: relative; cursor: pointer; }
    .upload.disabled { pointer-events: none; opacity: 0.6; }
    .upload input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
  `,
})
export class BusinessImages {
  private readonly api = inject(API_CLIENT);
  private readonly notifier = inject(Notifier);
  private readonly session = inject(SessionService);

  protected readonly kinds: ('logo' | 'cover')[] = ['logo', 'cover'];
  protected readonly urls = signal<Record<'logo' | 'cover', string | null>>({ logo: null, cover: null });
  protected readonly busy = signal<BusinessImageKind | null>(null);

  constructor() {
    effect(() => {
      if (this.session.currentBusiness()) untracked(() => void this.load());
    });
  }

  protected async upload(kind: BusinessImageKind, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.busy.set(kind);
    try {
      const { data, error } = await this.send(kind, await resizeImage(file, kind));
      if (error) this.notifier.error(error);
      else if (data) this.show(data);
      // F5-10: the home-screen icon of the booking page is made from the logo. Without it the logo is used.
      if (data && kind === 'logo') await this.send('icon', await resizeImage(file, 'icon'));
    } catch {
      // Not an image the browser can decode.
      this.notifier.error({ errors: { file: ['image.type'] } });
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(kind: BusinessImageKind): Promise<void> {
    this.busy.set(kind);
    const { data, error } = await this.api.DELETE('/api/tenancy/tenants/current/images/{kind}', { params: { path: { kind } } });
    this.busy.set(null);
    if (error) this.notifier.error(error);
    else if (data) this.show(data);
  }

  private send(kind: BusinessImageKind, image: Blob) {
    const form = new FormData();
    form.append('file', image, `${kind}.${image.type.split('/')[1]}`);
    return this.api.PUT('/api/tenancy/tenants/current/images/{kind}', {
      params: { path: { kind } },
      body: form as never,
      bodySerializer: (body) => body as unknown as FormData,
    });
  }

  private async load(): Promise<void> {
    const { data } = await this.api.GET('/api/tenancy/tenants/current');
    if (data) this.show(data);
  }

  private show(tenant: { logoUrl?: string | null; coverUrl?: string | null }): void {
    this.urls.set({ logo: assetUrl(tenant.logoUrl), cover: assetUrl(tenant.coverUrl) });
  }
}
