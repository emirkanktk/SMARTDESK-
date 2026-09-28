import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/guards/auth.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    // The public front page. Signed-in users go straight to their dashboard instead.
    path: '',
    pathMatch: 'full',
    canActivate: [guestGuard],
    title: 'SmartDesk · Office resources and reservations',
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · SmartDesk',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    // Both open to anyone: the reset link may be opened on a device that's still signed in.
    path: 'forgot-password',
    title: 'Forgot password · SmartDesk',
    loadComponent: () =>
      import('./features/password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Choose a new password · SmartDesk',
    loadComponent: () => import('./features/password/reset-password').then((m) => m.ResetPassword),
  },
  {
    // Every signed-in screen renders inside the shell. canActivateChild re-checks the
    // session on each navigation, so an expired token is caught before the screen loads.
    path: '',
    component: Shell,
    canActivate: [authGuard],
    canActivateChild: [authGuard],
    children: [
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
        path: 'resources/new',
        title: 'New resource · SmartDesk',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/resources/resource-form/resource-form').then((m) => m.ResourceForm),
      },
      {
        path: 'resources/:id/edit',
        title: 'Edit resource · SmartDesk',
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/resources/resource-form/resource-form').then((m) => m.ResourceForm),
      },
      {
        path: 'availability',
        title: 'Availability · SmartDesk',
        loadComponent: () =>
          import('./features/availability/availability').then((m) => m.Availability),
      },
      {
        path: 'calendar',
        title: 'Calendar · SmartDesk',
        loadComponent: () => import('./features/calendar/calendar').then((m) => m.Calendar),
      },
      {
        path: 'reservations/new',
        title: 'Book a resource · SmartDesk',
        loadComponent: () =>
          import('./features/reservation/reservation-form').then((m) => m.ReservationForm),
      },
      {
        // Owner or Admin; the form turns read-only for anyone else, and the API decides.
        path: 'reservations/:id/edit',
        title: 'Change reservation · SmartDesk',
        loadComponent: () =>
          import('./features/reservation/reservation-form').then((m) => m.ReservationForm),
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
      {
        path: 'admin/reports',
        title: 'Usage report · SmartDesk',
        canActivate: [adminGuard],
        loadComponent: () => import('./features/reports/usage-report').then((m) => m.UsageReport),
      },
    ],
  },
  // Unknown address: the front page, which forwards signed-in users to their dashboard.
  { path: '**', redirectTo: '' },
];
