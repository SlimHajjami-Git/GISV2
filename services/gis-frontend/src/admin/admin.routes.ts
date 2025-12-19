import { Routes } from '@angular/router';
import { AdminLoginComponent } from './pages/admin-login.component';
import { AdminDashboardComponent } from './pages/admin-dashboard.component';
import { AdminClientsComponent } from './pages/admin-clients.component';
import { AdminUsersComponent } from './pages/admin-users.component';
import { AdminHealthComponent } from './pages/admin-health.component';
import { AdminFeatureControlComponent } from './pages/admin-feature-control.component';
import { AdminEstimatesComponent } from './pages/admin-estimates.component';
import { AdminActivityComponent } from './pages/admin-activity.component';
import { AdminSettingsComponent } from './pages/admin-settings.component';

export const adminRoutes: Routes = [
  { path: '', redirectTo: 'login', pathMatch: 'full' },
  { path: 'login', component: AdminLoginComponent },
  { path: 'dashboard', component: AdminDashboardComponent },
  { path: 'clients', component: AdminClientsComponent },
  { path: 'users', component: AdminUsersComponent },
  { path: 'health', component: AdminHealthComponent },
  { path: 'features', component: AdminFeatureControlComponent },
  { path: 'estimates', component: AdminEstimatesComponent },
  { path: 'activity', component: AdminActivityComponent },
  { path: 'settings', component: AdminSettingsComponent },
];
