import { Routes } from '@angular/router';
import { adminGuard, authGuard, backofficeGuard } from './core/auth/guards';

export const routes: Routes = [
  {
    path: 'admin',
    canActivate: [backofficeGuard],
    loadComponent: () => import('./layouts/backoffice-layout/backoffice-layout').then((m) => m.BackofficeLayout),
    children: [
      { path: '', loadComponent: () => import('./features/admin/dashboard').then((m) => m.Dashboard) },
      // F1-13: Admin and Staff (Staff see only their own appointments).
      {
        path: 'calendario',
        loadComponent: () => import('./features/admin/calendar/calendar-page').then((m) => m.CalendarPage),
      },
      {
        path: 'sedi',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/locations/locations-page').then((m) => m.LocationsPage),
      },
      {
        path: 'servizi',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/services/services-page').then((m) => m.ServicesPage),
      },
      {
        path: 'staff',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/staff/staff-page').then((m) => m.StaffPage),
      },
      {
        path: 'staff/:id',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/staff/staff-schedule-page').then((m) => m.StaffSchedulePage),
      },
    ],
  },
  {
    path: '',
    loadComponent: () => import('./layouts/public-layout/public-layout').then((m) => m.PublicLayout),
    children: [
      { path: '', loadComponent: () => import('./features/home/home').then((m) => m.Home) },
      { path: 'login', loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
      {
        path: 'registra-attivita',
        canActivate: [authGuard],
        loadComponent: () => import('./features/auth/register-business').then((m) => m.RegisterBusiness),
      },
      // Legacy prefix, kept so early shared links keep working.
      { path: 'b/:slug', redirectTo: '/:slug' },
      // F1-12: the client's appointments with a business.
      {
        path: ':slug/prenotazioni',
        loadComponent: () => import('./features/booking/my-appointments').then((m) => m.MyAppointments),
      },
      // Public booking page of a business at the site root. Must stay after every fixed route;
      // fixed top-level paths must also be in TenantRules.ReservedSlugs (backend).
      { path: ':slug', loadComponent: () => import('./features/booking/booking-page').then((m) => m.BookingPage) },
      { path: '**', loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound) },
    ],
  },
];
