import { Routes } from '@angular/router';
import { authGuard, backofficeGuard } from './core/auth/guards';

export const routes: Routes = [
  {
    path: 'admin',
    canActivate: [backofficeGuard],
    loadComponent: () => import('./layouts/backoffice-layout/backoffice-layout').then((m) => m.BackofficeLayout),
    children: [{ path: '', loadComponent: () => import('./features/admin/dashboard').then((m) => m.Dashboard) }],
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
      { path: 'b/:slug', loadComponent: () => import('./features/booking/booking-page').then((m) => m.BookingPage) },
      { path: '**', loadComponent: () => import('./features/not-found/not-found').then((m) => m.NotFound) },
    ],
  },
];
