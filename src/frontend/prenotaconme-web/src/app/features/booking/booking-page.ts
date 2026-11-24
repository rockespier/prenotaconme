import { Component, effect, inject, input, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { API_CLIENT, Schemas } from '../../core/api/api-client';

/** Public booking page for a business: /b/:slug. Booking flow arrives in F1-10. */
@Component({
  selector: 'app-booking-page',
  imports: [TranslocoPipe],
  template: `
    @switch (state()) {
      @case ('loading') {
        <p>…</p>
      }
      @case ('notFound') {
        <h1>{{ 'booking.notFound' | transloco }}</h1>
      }
      @default {
        <h1>{{ tenant()?.name }}</h1>
        <p>{{ 'booking.comingSoon' | transloco }}</p>
      }
    }
  `,
})
export class BookingPage {
  private readonly api = inject(API_CLIENT);

  readonly slug = input.required<string>();

  protected readonly tenant = signal<Schemas['PublicTenantDto'] | null>(null);
  protected readonly state = signal<'loading' | 'ready' | 'notFound'>('loading');

  constructor() {
    effect(() => void this.load(this.slug()));
  }

  private async load(slug: string): Promise<void> {
    this.state.set('loading');
    const { data } = await this.api.GET('/api/tenancy/tenants/{slug}', {
      params: { path: { slug } },
      // Public page: never send the backoffice tenant selection.
      headers: { 'X-Tenant': '' },
    });
    this.tenant.set(data ?? null);
    this.state.set(data ? 'ready' : 'notFound');
  }
}
