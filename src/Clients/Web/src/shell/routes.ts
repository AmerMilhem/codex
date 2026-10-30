import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'finance/:tenant/:company/journals/:id/correct',
    loadComponent: () =>
      import('../features/finance/journal-correction').then((m) => m.JournalCorrection),
  },
  { path: '', loadComponent: () => import('../features/home').then((m) => m.Home) },
  {
    path: 'workspace',
    loadComponent: () => import('../features/workspace').then((m) => m.Workspace),
  },
  {
    path: 'finance/:tenant/:company/journals',
    loadComponent: () => import('../features/finance/journals').then((m) => m.Journals),
  },
  {
    path: 'finance/:tenant/:company/journals/:id',
    loadComponent: () => import('../features/finance/journals').then((m) => m.Journals),
  },
  {
    path: 'finance/:tenant/:company/trial-balance',
    loadComponent: () => import('../features/finance/trial-balance').then((m) => m.TrialBalance),
  },
  {
    path: 'finance/:tenant/:company/general-ledger',
    loadComponent: () => import('../features/finance/general-ledger').then((m) => m.GeneralLedger),
  },
  {
    path: 'finance/:tenant/:company/periods',
    loadComponent: () => import('../features/finance/fiscal-periods').then((m) => m.FiscalPeriods),
  },
  {
    path: 'finance/:tenant/:company/accounts',
    loadComponent: () => import('../features/finance/account-setup').then((m) => m.AccountSetup),
  },
  {
    path: 'finance/:tenant/:company/books',
    loadComponent: () =>
      import('../features/finance/accounting-books').then((m) => m.AccountingBooks),
  },
  { path: '**', redirectTo: '' },
];
