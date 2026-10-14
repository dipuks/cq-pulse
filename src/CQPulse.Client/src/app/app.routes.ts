import { Routes } from '@angular/router';
import { authGuard } from './core/auth';
import { Shell } from './layout/shell';

// Feature pages are lazy-loaded; the shell and guard load eagerly.
export const routes: Routes = [
  { path: 'login', title: 'Sign in · CQ Pulse', loadComponent: () => import('./features/auth/login.page').then(m => m.LoginPage) },
  {
    path: '', component: Shell, canActivate: [authGuard], children: [
      { path: 'dashboard', title: 'Dashboard · CQ Pulse', loadComponent: () => import('./features/dashboard/dashboard.page').then(m => m.DashboardPage) },
      { path: 'projects', title: 'Projects · CQ Pulse', loadComponent: () => import('./features/projects/projects.page').then(m => m.ProjectsPage) },
      { path: 'projects/new', title: 'New project · CQ Pulse', loadComponent: () => import('./features/projects/project-editor.page').then(m => m.ProjectEditorPage) },
      { path: 'projects/:projectId', title: 'Project · CQ Pulse', loadComponent: () => import('./features/projects/project-detail.page').then(m => m.ProjectDetailPage) },
      { path: 'projects/:projectId/suites', title: 'Suites · CQ Pulse', loadComponent: () => import('./features/suites/project-suites.page').then(m => m.ProjectSuitesPage) },
      { path: 'suites/:suiteId', title: 'Suite · CQ Pulse', data: { view: 'overview' }, loadComponent: () => import('./features/suites/suite.page').then(m => m.SuitePage) },
      { path: 'suites/:suiteId/cases', title: 'Test cases · CQ Pulse', data: { view: 'cases' }, loadComponent: () => import('./features/suites/suite.page').then(m => m.SuitePage) },
      { path: 'test-cases/:testCaseId', title: 'Test case · CQ Pulse', loadComponent: () => import('./features/test-cases/test-case.page').then(m => m.TestCasePage) },
      { path: 'runs', title: 'Test runs · CQ Pulse', loadComponent: () => import('./features/runs/runs.page').then(m => m.RunsPage) },
      { path: 'runs/:runId', title: 'Run detail · CQ Pulse', loadComponent: () => import('./features/runs/run-detail.page').then(m => m.RunDetailPage) },
      { path: 'simulator', title: 'Simulator · CQ Pulse', loadComponent: () => import('./features/simulator/simulator.page').then(m => m.SimulatorPage) },
      { path: 'analytics', title: 'Analytics · CQ Pulse', loadComponent: () => import('./features/analytics/analytics.page').then(m => m.AnalyticsPage) },
      { path: 'settings', title: 'Settings · CQ Pulse', loadComponent: () => import('./features/settings/settings.page').then(m => m.SettingsPage) },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: 'not-found', title: 'Not found · CQ Pulse', loadComponent: () => import('./features/not-found/not-found.page').then(m => m.NotFoundPage) },
  { path: '**', redirectTo: 'not-found' }
];
