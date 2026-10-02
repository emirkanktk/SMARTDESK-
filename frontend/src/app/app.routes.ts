import { Routes } from '@angular/router';
import {
  adminGuard,
  authGuard,
  guestGuard,
  managerGuard,
  resourceEditorGuard,
  sessionGuard,
} from './core/guards/auth.guard';
import { Shell } from './layout/shell/shell';

export const routes: Routes = [
  {
    // The public front page. Signed-in users go straight to their dashboard instead.
    path: '',
    pathMatch: 'full',
    canActivate: [guestGuard],
    title: $localize`:@@title.home:SmartDesk · Office resources and reservations`,
    loadComponent: () => import('./features/home/home').then((m) => m.Home),
  },
  {
    path: 'login',
    canActivate: [guestGuard],
    title: $localize`:@@title.login:Sign in · SmartDesk`,
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    title: $localize`:@@title.register:Create an account · SmartDesk`,
    loadComponent: () => import('./features/register/register').then((m) => m.Register),
  },
  {
    // Both open to anyone: the reset link may be opened on a device that's still signed in.
    path: 'forgot-password',
    title: $localize`:@@title.forgotPassword:Forgot password · SmartDesk`,
    loadComponent: () =>
      import('./features/password/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: $localize`:@@title.resetPassword:Choose a new password · SmartDesk`,
    loadComponent: () => import('./features/password/reset-password').then((m) => m.ResetPassword),
  },
  {
    // Outside the shell: while a temporary password must be replaced, nothing else is reachable.
    path: 'change-password',
    canActivate: [sessionGuard],
    title: $localize`:@@title.changePassword:Change your password · SmartDesk`,
    loadComponent: () =>
      import('./features/password/change-password').then((m) => m.ChangePassword),
  },
  {
    // Outside the shell, so only the sheet with the QR code prints (ADR-0030).
    path: 'resources/:id/check-in-poster',
    canActivate: [authGuard, resourceEditorGuard],
    title: $localize`:@@title.checkInPoster:QR code for checking in · SmartDesk`,
    loadComponent: () =>
      import('./features/check-in-poster/check-in-poster').then((m) => m.CheckInPoster),
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
        title: $localize`:@@title.dashboard:Dashboard · SmartDesk`,
        loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
      },
      {
        path: 'resources',
        title: $localize`:@@title.resources:Resources · SmartDesk`,
        loadComponent: () => import('./features/resources/resources').then((m) => m.Resources),
      },
      {
        path: 'resources/new',
        title: $localize`:@@title.newResource:New resource · SmartDesk`,
        canActivate: [resourceEditorGuard],
        loadComponent: () =>
          import('./features/resources/resource-form/resource-form').then((m) => m.ResourceForm),
      },
      {
        path: 'resources/:id/edit',
        title: $localize`:@@title.editResource:Edit resource · SmartDesk`,
        canActivate: [resourceEditorGuard],
        loadComponent: () =>
          import('./features/resources/resource-form/resource-form').then((m) => m.ResourceForm),
      },
      {
        path: 'availability',
        title: $localize`:@@title.availability:Availability · SmartDesk`,
        loadComponent: () =>
          import('./features/availability/availability').then((m) => m.Availability),
      },
      {
        path: 'calendar',
        title: $localize`:@@title.calendar:Calendar · SmartDesk`,
        loadComponent: () => import('./features/calendar/calendar').then((m) => m.Calendar),
      },
      {
        path: 'reservations/new',
        title: $localize`:@@title.newReservation:Book a resource · SmartDesk`,
        loadComponent: () =>
          import('./features/reservation/reservation-form').then((m) => m.ReservationForm),
      },
      {
        // Owner or Admin; the form turns read-only for anyone else, and the API decides.
        path: 'reservations/:id/edit',
        title: $localize`:@@title.editReservation:Change reservation · SmartDesk`,
        loadComponent: () =>
          import('./features/reservation/reservation-form').then((m) => m.ReservationForm),
      },
      {
        // Where a resource's QR code leads (ADR-0030). Signed out, the guard sends the phone
        // to sign in first and back here afterwards, code and all.
        path: 'check-in/:resourceId',
        title: $localize`:@@title.checkIn:Check in · SmartDesk`,
        loadComponent: () => import('./features/check-in/check-in').then((m) => m.CheckIn),
      },
      {
        path: 'reservations/mine',
        title: $localize`:@@title.myReservations:My reservations · SmartDesk`,
        loadComponent: () =>
          import('./features/my-reservations/my-reservations').then((m) => m.MyReservations),
      },
      {
        path: 'profile',
        title: $localize`:@@title.profile:My profile · SmartDesk`,
        loadComponent: () => import('./features/profile/profile').then((m) => m.Profile),
      },
      {
        path: 'admin',
        title: $localize`:@@title.admin:Admin · SmartDesk`,
        canActivate: [adminGuard],
        loadComponent: () => import('./features/admin/admin').then((m) => m.Admin),
      },
      {
        path: 'admin/users',
        title: $localize`:@@title.users:Users · SmartDesk`,
        canActivate: [adminGuard],
        loadComponent: () => import('./features/users/users').then((m) => m.Users),
      },
      {
        path: 'team',
        title: $localize`:@@title.team:My team · SmartDesk`,
        canActivate: [managerGuard],
        loadComponent: () => import('./features/team/team').then((m) => m.Team),
      },
      {
        path: 'admin/reports',
        title: $localize`:@@title.report:Usage report · SmartDesk`,
        canActivate: [adminGuard],
        loadComponent: () => import('./features/reports/usage-report').then((m) => m.UsageReport),
      },
      {
        path: 'admin/blocks',
        title: $localize`:@@title.blocks:Blocked windows · SmartDesk`,
        canActivate: [adminGuard],
        loadComponent: () =>
          import('./features/resource-blocks/resource-blocks').then((m) => m.ResourceBlocks),
      },
    ],
  },
  // Unknown address: the front page, which forwards signed-in users to their dashboard.
  { path: '**', redirectTo: '' },
];
