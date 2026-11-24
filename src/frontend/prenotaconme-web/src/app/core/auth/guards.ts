import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from '../session/session.service';
import { AuthService } from './auth.service';

// Note: inject() only works before the first await of a guard.

/** Requires a signed-in user; otherwise goes to /login and comes back afterwards. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.whenReady();
  return auth.isSignedIn() ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Backoffice: signed in and Admin/Staff of at least one business; otherwise offers to register one. */
export const backofficeGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const session = inject(SessionService);
  const router = inject(Router);

  await auth.whenReady();
  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  await session.load();
  return session.businesses().length > 0 ? true : router.createUrlTree(['/registra-attivita']);
};

/** Setup pages (locations, services, staff): Admin of the selected business. Runs after backofficeGuard. */
export const adminGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  return session.isAdmin() ? true : inject(Router).createUrlTree(['/admin']);
};
