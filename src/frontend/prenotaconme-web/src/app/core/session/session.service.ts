import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { API_CLIENT, Schemas } from '../api/api-client';
import { AuthService } from '../auth/auth.service';
import { TenantSelection } from './tenant-selection';

type Me = Schemas['MeDto'];
type Membership = Schemas['MembershipDto'];

export interface BusinessAccess {
  slug: string;
  name: string;
  roles: Membership['role'][];
}

/** Profile of the signed-in user (from /api/users/me) and the businesses they can manage. */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(API_CLIENT);
  private readonly auth = inject(AuthService);
  private readonly tenant = inject(TenantSelection);

  readonly me = signal<Me | null>(null);

  /** Businesses where the user is Admin or Staff (backoffice access), one entry per business. */
  readonly businesses = computed<BusinessAccess[]>(() => {
    const map = new Map<string, BusinessAccess>();
    for (const m of this.me()?.memberships ?? []) {
      if (m.role === 'Client') continue;
      const entry = map.get(m.tenantSlug) ?? { slug: m.tenantSlug, name: m.tenantName, roles: [] };
      entry.roles.push(m.role);
      map.set(m.tenantSlug, entry);
    }
    return [...map.values()];
  });

  readonly currentBusiness = computed(() => this.businesses().find((b) => b.slug === this.tenant.slug()) ?? null);

  constructor() {
    // Signing out (or switching account) drops the cached profile.
    effect(() => {
      if (!this.auth.user()) {
        this.me.set(null);
      }
    });
  }

  /** Loads (and provisions) the user's profile. Returns null when not signed in or on error. */
  async load(force = false): Promise<Me | null> {
    if (!this.auth.user()) return null;
    if (this.me() && !force) return this.me();

    const { data } = await this.api.GET('/api/users/me');
    this.me.set(data ?? null);
    this.ensureValidSelection();
    return this.me();
  }

  selectBusiness(slug: string): void {
    this.tenant.select(slug);
  }

  async signOut(): Promise<void> {
    this.tenant.select(null);
    await this.auth.signOut();
  }

  /** Keeps the selected business valid: falls back to the first one the user can manage. */
  private ensureValidSelection(): void {
    const businesses = this.businesses();
    if (!businesses.some((b) => b.slug === this.tenant.slug())) {
      this.tenant.select(businesses[0]?.slug ?? null);
    }
  }
}
