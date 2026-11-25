import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { API_CLIENT, Schemas } from '../../core/api/api-client';

/** F2-03: the business's current announcements, at the top of its public pages. */
@Component({
  selector: 'app-announcement-banner',
  imports: [MatIconModule],
  template: `
    @for (a of announcements(); track a.id) {
      <aside class="banner" role="note">
        <mat-icon aria-hidden="true">campaign</mat-icon>
        <div>
          <strong>{{ a.title }}</strong>
          <p>{{ a.message }}</p>
        </div>
      </aside>
    }
  `,
  styles: `
    .banner {
      display: flex;
      gap: var(--space-3);
      margin: 0 0 var(--space-4);
      padding: var(--space-3) var(--space-4);
      border-radius: var(--radius-l);
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    mat-icon { flex: none; margin-top: 2px; }
    strong { display: block; font-weight: 600; }
    p { margin: var(--space-1) 0 0; white-space: pre-line; font: var(--mat-sys-body-medium); }
  `,
})
export class AnnouncementBanner {
  private readonly api = inject(API_CLIENT);

  /** Business slug (sent as X-Tenant). */
  readonly slug = input.required<string>();

  protected readonly announcements = signal<Schemas['PublicAnnouncementDto'][]>([]);

  constructor() {
    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug));
    });
  }

  private async load(slug: string): Promise<void> {
    const { data } = await this.api.GET('/api/notifications/announcements/current', { headers: { 'X-Tenant': slug } });
    this.announcements.set(data ?? []);
  }
}
