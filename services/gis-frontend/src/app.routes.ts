import { Routes } from '@angular/router';
import { LandingComponent } from './components/landing.component';
import { LoginComponent } from './components/login.component';
import { DashboardComponent } from './components/dashboard.component';
import { VehiclesComponent } from './components/vehicles.component';
import { EmployeesComponent } from './components/employees.component';
import { GpsComponent } from './components/gps.component';
import { SubscriptionComponent } from './components/subscription.component';
import { MonitoringComponent } from './components/monitoring.component';
import { ReportsComponent } from './components/reports.component';
import { MonthlyReportComponent } from './components/monthly-report.component';
import { GeofencesComponent } from './components/geofences.component';
import { NotificationsComponent } from './components/notifications.component';
import { GPSDevicesComponent } from './components/gps-devices.component';
import { MaintenanceComponent } from './components/maintenance.component';
import { VehicleCostsComponent } from './components/vehicle-costs.component';
import { ProfileComponent } from './components/profile.component';
import { SettingsComponent } from './components/settings.component';
import { UserManagementComponent } from './components/user-management.component';
import { SuppliersComponent } from './components/suppliers.component';
import { DocumentsComponent } from './components/documents.component';
import { AccidentClaimsComponent } from './components/accident-claims.component';
import { MaintenanceTemplatesComponent } from './components/maintenance-templates.component';
import { FleetManagementComponent } from './components/fleet-management.component';
import { RepairsComponent } from './components/repairs.component';
import { ExpensesComponent } from './components/expenses.component';
import { CarburantComponent } from './components/carburant.component';
import { adminRoutes } from './admin/admin.routes';
import { AuthGuard } from './guards/auth.guard';
import { FeatureGuard } from './guards/feature.guard';
import { SystemAdminGuard } from './guards/system-admin.guard';

export const routes: Routes = [
  // Public routes (no auth required)
  { path: '', component: LandingComponent },
  { path: 'login', component: LoginComponent },
  // Protected routes - Dashboard (always accessible when logged in)
  { path: 'dashboard', component: DashboardComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'dashboard' } },
  
  // Vehicles module
  { path: 'units', component: VehiclesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'vehicles' } },
  { path: 'vehicles', component: VehiclesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'vehicles' } },
  
  // Employees module
  { path: 'drivers', component: EmployeesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'employees' } },
  { path: 'employees', component: EmployeesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'employees' } },
  
  // GPS module (requires monitoring)
  { path: 'gps', component: GpsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'monitoring' } },
  { path: 'gps-devices', component: GPSDevicesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'monitoring' } },
  
  // Maintenance module
  { path: 'maintenance', component: MaintenanceComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'entretiens-maitres', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'maintenance-templates', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'entretien-programmable', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  
  // Costs module
  { path: 'costs', component: VehicleCostsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'expenses', component: ExpensesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'depenses', component: ExpensesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'carburant', component: CarburantComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  
  // Subscription (always accessible)
  { path: 'subscription', component: SubscriptionComponent, canActivate: [AuthGuard] },
  
  // Monitoring module
  { path: 'monitoring', component: MonitoringComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'monitoring' } },
  
  // Reports module
  { path: 'reports', component: ReportsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'reports' } },
  { path: 'reports/monthly', component: MonthlyReportComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'reports' } },
  
  // Geofences module
  { path: 'geofences', component: GeofencesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'geofences' } },
  
  // Notifications (always accessible when logged in)
  { path: 'notifications', component: NotificationsComponent, canActivate: [AuthGuard] },
  
  // Profile (always accessible when logged in)
  { path: 'profile', component: ProfileComponent, canActivate: [AuthGuard] },
  
  // Settings module
  { path: 'settings', component: SettingsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'settings' } },
  
  // Users module
  { path: 'users', component: UserManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'users' } },
  
  // Suppliers module
  { path: 'suppliers', component: SuppliersComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'suppliers' } },
  { path: 'fournisseurs', component: SuppliersComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'suppliers' } },
  
  // Documents module
  { path: 'documents', component: DocumentsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'documents' } },
  { path: 'echeances', component: DocumentsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'documents' } },
  
  // Accidents module
  { path: 'accidents', component: AccidentClaimsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'accidents' } },
  { path: 'sinistres', component: AccidentClaimsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'accidents' } },
  
  // Fleet management module
  { path: 'fleet-management', component: FleetManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'fleet_management' } },
  { path: 'gestion-flotte', component: FleetManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'fleet_management' } },
  
  // Repairs module (part of maintenance)
  { path: 'repairs', component: RepairsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'reparations', component: RepairsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  
  // Admin routes (login public, rest requires system admin)
  { path: 'admin', children: adminRoutes },
  
  // Fallback
  { path: '**', redirectTo: '' },
];
