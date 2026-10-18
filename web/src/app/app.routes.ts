import { Routes } from '@angular/router';
import { allowedGuard, authGuard, setupGuard } from './core/guards';
import { settingsMatcher } from './features/settings/settings-sections';

// PRD 7.7. The setup wizard itself comes with M3.
export const routes: Routes = [
  {
    path: 'sign-in',
    title: 'NAMA · Sign in',
    loadComponent: () => import('./features/sign-in/sign-in').then((m) => m.SignIn),
  },
  // AUTH-06: an invite link. Public: the page signs in first, then joins.
  {
    path: 'invite/:token',
    title: 'NAMA · Invite',
    loadComponent: () => import('./features/sign-in/sign-in').then((m) => m.SignIn),
  },
  {
    path: 'no-access',
    title: 'NAMA · Access not allowed',
    loadComponent: () => import('./features/no-access/no-access').then((m) => m.NoAccess),
  },
  {
    path: 'setup',
    title: 'NAMA · Setup',
    canActivate: [authGuard, allowedGuard],
    loadComponent: () => import('./features/setup/setup').then((m) => m.Setup),
  },
  {
    path: '',
    pathMatch: 'full',
    title: 'NAMA · Dashboard',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'months',
    title: 'NAMA · Months',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/months/months').then((m) => m.Months),
  },
  {
    path: 'add',
    title: 'NAMA · Add',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/add-entry/add-entry').then((m) => m.AddEntry),
  },
  {
    path: 'entries',
    title: 'NAMA · Entries',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/entries/entries').then((m) => m.Entries),
  },
  {
    path: 'plan',
    title: 'NAMA · Plan',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/plan/plan').then((m) => m.Plan),
  },
  {
    matcher: settingsMatcher,
    title: 'NAMA · Settings',
    canActivate: [authGuard, setupGuard],
    loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
  },
  { path: '**', redirectTo: '' },
];
