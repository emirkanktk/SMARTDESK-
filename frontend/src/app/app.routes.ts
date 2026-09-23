import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    path: 'login',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'resources',
    canActivate: [authGuard],
    loadComponent: () => import('./features/resources/resources').then((m) => m.Resources),
  },
  {
    path: 'availability',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/availability/availability').then((m) => m.Availability),
  },
  {
    path: 'reservations/new',
    canActivate: [authGuard],
    loadComponent: () => import('./features/reservation/reservation').then((m) => m.Reservation),
  },
  {
    path: 'reservations/mine',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/my-reservations/my-reservations').then((m) => m.MyReservations),
  },
  {
    path: 'admin',
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
  },
  { path: '**', redirectTo: 'dashboard' },
];
