import { InjectionToken } from '@angular/core';
import createClient, { type Client } from 'openapi-fetch';
import { environment } from '../../../environments/environment';
import type { paths } from './schema';

/**
 * Typed API client generated from the backend OpenAPI document.
 * Regenerate types after backend changes: `dotnet build` (backend) then `npm run api:generate`.
 */
export type ApiClient = Client<paths>;

export const API_CLIENT = new InjectionToken<ApiClient>('API_CLIENT', {
  providedIn: 'root',
  factory: () => createClient<paths>({ baseUrl: environment.apiUrl }),
});
