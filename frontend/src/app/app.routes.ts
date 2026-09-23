import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/guards/auth.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · SmartDesk',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    // Every signed-in screen renders inside the shell. canActivateChild re-checks the
    // session on each navigation, so an expired token is caught before the screen loads.
    path: '',
    component: Shell,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        title: 'Dashboard · SmartDesk',
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'resources',
        title: 'Resources · SmartDesk',
        loadComponent: () => import('./features/resources/resources').then((m) => m.Resources),
      },
      {
        path: 'availability',
        title: 'Availability · SmartDesk',
        loadComponent: () =>
          import('./features/availability/availability').then((m) => m.Availability),
      },
      {
        path: 'reservations/new',
        title: 'New reservation · SmartDesk',
        loadComponent: () =>
          import('./features/reservation/reservation').then((m) => m.Reservation),
      },
      {
        path: 'reservations/mine',
        title: 'My reservations · SmartDesk',
        loadComponent: () =>
          import('./features/my-reservations/my-reservations').then((m) => m.MyReservations),
      },
      {
        path: 'admin',
        title: 'Admin · SmartDesk',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
      },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
