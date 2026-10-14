import { Routes } from '@angular/router';
import { LoginPage, DashboardPage, ProjectsPage, ProjectEditorPage, ProjectSuitesPage, SuitePage, CasePage, RunsPage, RunDetailPage, SimulatorPage, AnalyticsPage, SettingsPage, NotFoundPage } from './pages';
import { authGuard } from './core';
import { Shell } from './shell';

export const routes: Routes = [
  { path: 'login', component: LoginPage },
  { path: '', component: Shell, canActivate: [authGuard], children: [
    { path: 'dashboard', component: DashboardPage },
    { path: 'projects', component: ProjectsPage },
    { path: 'projects/new', component: ProjectEditorPage },
    { path: 'projects/:projectId', component: ProjectEditorPage },
    { path: 'projects/:projectId/suites', component: ProjectSuitesPage },
    { path: 'suites/:suiteId', component: SuitePage },
    { path: 'suites/:suiteId/cases', component: SuitePage },
    { path: 'test-cases/:testCaseId', component: CasePage },
    { path: 'runs', component: RunsPage },
    { path: 'runs/:runId', component: RunDetailPage },
    { path: 'simulator', component: SimulatorPage },
    { path: 'analytics', component: AnalyticsPage },
    { path: 'settings', component: SettingsPage },
    { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
  ]},
  { path: 'not-found', component: NotFoundPage },
  { path: '**', redirectTo: 'not-found' }
];
