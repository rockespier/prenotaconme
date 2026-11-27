import { inject, InjectionToken } from '@angular/core';
import createClient, { type Client } from 'openapi-fetch';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';
import { TenantSelection } from '../session/tenant-selection';
import type { components, paths } from './schema';

/**
 * Typed API client generated from the backend OpenAPI document.
 * Regenerate types after backend changes: `dotnet build` (backend) then `npm run api:generate`.
 *
 * Every request carries the Firebase ID token (when signed in) and the selected business as X-Tenant,
 * unless the caller sets X-Tenant explicitly.
 */
export type ApiClient = Client<paths>;
export type Schemas = components['schemas'];

export const API_CLIENT = new InjectionToken<ApiClient>('API_CLIENT', {
  providedIn: 'root',
  factory: () => {
    const auth = inject(AuthService);
    const tenant = inject(TenantSelection);
    const client = createClient<paths>({ baseUrl: environment.apiUrl });

    client.use({
      async onRequest({ request }) {
        const token = await auth.idToken();
        if (token) {
          request.headers.set('Authorization', `Bearer ${token}`);
        }

        const slug = tenant.slug();
        if (slug && !request.headers.has('X-Tenant')) {
          request.headers.set('X-Tenant', slug);
        }

        return request;
      },
    });

    return client;
  },
});

/** Absolute URL of a file the API serves (paths in DTOs are relative, e.g. business images); null stays null. */
export function assetUrl(path: string | null | undefined): string | null {
  return path ? environment.apiUrl + path : null;
}
