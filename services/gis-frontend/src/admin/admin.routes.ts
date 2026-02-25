import { Routes } from '@angular/router';
import { AdminLoginComponent } from './pages/admin-login.component';
import { AdminDashboardComponent } from './pages/admin-dashboard.component';
import { AdminClientsComponent } from './pages/admin-clients.component';
import { AdminUsersComponent } from './pages/admin-users.component';
import { AdminVehiclesComponent } from './pages/admin-vehicles.component';
import { AdminHealthComponent } from './pages/admin-health.component';
import { AdminFeatureControlComponent } from './pages/admin-feature-control.component';
import { AdminEstimatesComponent } from './pages/admin-estimates.component';
import { AdminActivityComponent } from './pages/admin-activity.component';
import { AdminSettingsComponent } from './pages/admin-settings.component';
import { AdminSubscriptionsComponent } from './pages/admin-subscriptions.component';
import { AdminRolesComponent } from './pages/admin-roles.component';
import { AdminCompanyDetailsComponent } from './pages/admin-company-details.component';
import { AdminBrandsComponent } from './pages/admin-brands.component';
import { AdminPartsComponent } from './pages/admin-parts.component';
import { AdminNotificationsComponent } from './pages/admin-notifications.component';
import { AdminMonitoringComponent } from './pages/admin-monitoring.component';
import { AdminReportsComponent } from './pages/admin-reports.component';
import { AdminGuard } from './guards/admin.guard';

export const adminRoutes: Routes = [
  // Public admin routes
  { path: '', redirectTo: 'login', pathMatch: 'full' },
  { path: 'login', component: AdminLoginComponent },
  
  // Protected admin routes (requires admin authentication)
  { path: 'dashboard', component: AdminDashboardComponent, canActivate: [AdminGuard] },
  { path: 'clients', component: AdminClientsComponent, canActivate: [AdminGuard] },
  { path: 'clients/:id', component: AdminCompanyDetailsComponent, canActivate: [AdminGuard] },
  { path: 'subscriptions', component: AdminSubscriptionsComponent, canActivate: [AdminGuard] },
  { path: 'roles', component: AdminRolesComponent, canActivate: [AdminGuard] },
  { path: 'users', component: AdminUsersComponent, canActivate: [AdminGuard] },
  { path: 'vehicles', component: AdminVehiclesComponent, canActivate: [AdminGuard] },
  { path: 'brands', component: AdminBrandsComponent, canActivate: [AdminGuard] },
  { path: 'parts', component: AdminPartsComponent, canActivate: [AdminGuard] },
  { path: 'health', component: AdminHealthComponent, canActivate: [AdminGuard] },
  { path: 'features', component: AdminFeatureControlComponent, canActivate: [AdminGuard] },
  { path: 'estimates', component: AdminEstimatesComponent, canActivate: [AdminGuard] },
  { path: 'activity', component: AdminActivityComponent, canActivate: [AdminGuard] },
  { path: 'notifications', component: AdminNotificationsComponent, canActivate: [AdminGuard] },
  { path: 'settings', component: AdminSettingsComponent, canActivate: [AdminGuard] },
  { path: 'monitoring', component: AdminMonitoringComponent, canActivate: [AdminGuard] },
  { path: 'reports', component: AdminReportsComponent, canActivate: [AdminGuard] },
];
