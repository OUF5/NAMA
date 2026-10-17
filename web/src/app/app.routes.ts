import { Routes } from '@angular/router';

// PRD 7.7. Sign-in, setup and no-access routes come with M1 and M3.
export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    title: 'NAMA · Dashboard',
    loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'months',
    title: 'NAMA · Months',
    loadComponent: () => import('./features/months/months').then((m) => m.Months),
  },
  {
    path: 'add',
    title: 'NAMA · Add',
    loadComponent: () => import('./features/add-entry/add-entry').then((m) => m.AddEntry),
  },
  {
    path: 'entries',
    title: 'NAMA · Entries',
    loadComponent: () => import('./features/entries/entries').then((m) => m.Entries),
  },
  {
    path: 'plan',
    title: 'NAMA · Plan',
    loadComponent: () => import('./features/plan/plan').then((m) => m.Plan),
  },
  {
    path: 'settings',
    title: 'NAMA · Settings',
    loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
  },
  { path: '**', redirectTo: '' },
];
