import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'tenant';

/**
 * Business (tenant slug) the backoffice is working on. Sent as X-Tenant on API calls.
 * Kept separate from SessionService so the API client can read it without a circular dependency.
 */
@Injectable({ providedIn: 'root' })
export class TenantSelection {
  readonly slug = signal<string | null>(globalThis.localStorage?.getItem(STORAGE_KEY) ?? null);

  select(slug: string | null): void {
    this.slug.set(slug);
    if (slug) {
      globalThis.localStorage?.setItem(STORAGE_KEY, slug);
    } else {
      globalThis.localStorage?.removeItem(STORAGE_KEY);
    }
  }
}
