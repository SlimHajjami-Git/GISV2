import { Routes } from '@angular/router';
import { LandingComponent } from './components/landing.component';
import { LoginComponent } from './components/login.component';
import { DashboardComponent } from './components/dashboard.component';
import { VehiclesComponent } from './components/vehicles.component';
import { EmployeesComponent } from './components/employees.component';
import { GpsComponent } from './components/gps.component';
import { SubscriptionComponent } from './components/subscription.component';
import { MonitoringComponent } from './components/monitoring.component';
import { MultiPlaybackComponent } from './components/multi-playback.component';
import { ReportsComponent } from './components/reports.component';
import { MonthlyReportComponent } from './components/monthly-report.component';
import { GeofencesComponent } from './components/geofences.component';
import { NotificationsComponent } from './components/notifications.component';
import { GPSDevicesComponent } from './components/gps-devices.component';
import { VehicleCostsComponent } from './components/vehicle-costs.component';
import { ProfileComponent } from './components/profile.component';
import { SettingsComponent } from './components/settings.component';
import { UserManagementComponent } from './components/user-management.component';
import { SuppliersComponent } from './components/suppliers.component';
import { DocumentsComponent } from './components/documents.component';
// Calypso 7 — AccidentClaimsComponent + accident-claim-form removed; the
// unified accident timeline lives in AccidentReportsListComponent +
// AccidentReportComponent. /accidents and /sinistres now redirect.
import { AccidentReportComponent } from './components/accident-report.component';
import { AccidentReportsListComponent } from './components/accident-reports-list.component';
import { MaintenanceTemplatesComponent } from './components/maintenance-templates.component';
import { FleetManagementComponent } from './components/fleet-management.component';
import { RepairsComponent } from './components/repairs.component';
import { ExpensesComponent } from './components/expenses.component';
import { CarburantComponent } from './components/carburant.component';
import { ToursComponent } from './components/tours.component';
import { DeviceCheckComponent } from './components/device-check.component';
import { SecuriteComponent } from './components/securite.component';
import { adminRoutes } from './admin/admin.routes';
import { AuthGuard } from './guards/auth.guard';
import { FeatureGuard } from './guards/feature.guard';
import { SystemAdminGuard } from './guards/system-admin.guard';
import { SecuriteGuard } from './guards/securite.guard';
import { LocationCompanyGuard } from './guards/location-company.guard';
import { VehicleLoansComponent } from './components/vehicle-loans.component';
import { PrivacyPolicyComponent } from './components/privacy-policy.component';
import { TowingComponent } from './components/towing.component';


export const routes: Routes = [
  // Public routes (no auth required)
  { path: '', component: LandingComponent },
  { path: 'login', component: LoginComponent },
  { path: 'device-check', component: DeviceCheckComponent },
  { path: 'politique-de-confidentialite', component: PrivacyPolicyComponent },
  { path: 'privacy-policy', redirectTo: 'politique-de-confidentialite', pathMatch: 'full' },
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
  { path: 'entretiens-maitres', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'maintenance-templates', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'entretien-programmable', component: MaintenanceTemplatesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  
  // Costs module
  { path: 'costs', component: VehicleCostsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'expenses', component: ExpensesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'depenses', component: ExpensesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'costs' } },
  { path: 'carburant', component: CarburantComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'carburant' } },
  
  // Subscription (always accessible)
  { path: 'subscription', component: SubscriptionComponent, canActivate: [AuthGuard] },
  
  // Monitoring module
  { path: 'monitoring', component: MonitoringComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'monitoring' } },
  { path: 'playback', component: MonitoringComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'playback', view: 'playback' } },
  { path: 'multi-playback', component: MultiPlaybackComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'playback' } },
  
  // Reports module
  { path: 'reports', component: ReportsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'reports' } },
  { path: 'reports/monthly', component: MonthlyReportComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'reports' } },
  
  // Geofences module
  { path: 'geofences', component: GeofencesComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'geofences' } },
  
  // Security (device events)
  { path: 'securite', component: SecuriteComponent, canActivate: [AuthGuard, SecuriteGuard] },

  // Remorquages (standalone tow detection — engine-off + speed + displacement)
  { path: 'remorquages', component: TowingComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'monitoring' } },

  // Notifications (always accessible when logged in)
  { path: 'notifications', component: NotificationsComponent, canActivate: [AuthGuard] },
  
  // Profile (always accessible when logged in)
  { path: 'profile', component: ProfileComponent, canActivate: [AuthGuard] },
  
  // Settings module
  { path: 'settings', component: SettingsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'settings' } },

  // Calypso 7 — la page Alertes email vit maintenant comme onglet dans
  // /users (UserManagementComponent), donc on redirige les anciens liens.
  { path: 'alertes-email', redirectTo: '/users', pathMatch: 'full' },
  { path: 'alert-emails', redirectTo: '/users', pathMatch: 'full' },
  
  // Users module
  { path: 'users', component: UserManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'users' } },
  
  // Suppliers module
  { path: 'suppliers', component: SuppliersComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'suppliers' } },
  { path: 'fournisseurs', component: SuppliersComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'suppliers' } },
  
  // Documents module
  { path: 'documents', component: DocumentsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'documents' } },
  { path: 'echeances', component: DocumentsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'documents' } },
  
  // Accidents module — Calypso 7 unified timeline.
  // /accidents and /sinistres redirect to the new list page so existing
  // bookmarks and menu links keep working.
  { path: 'accidents',         redirectTo: '/accident-reports', pathMatch: 'full' },
  { path: 'sinistres',         redirectTo: '/accident-reports', pathMatch: 'full' },
  { path: 'accident-reports',  component: AccidentReportsListComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'accidents' } },
  { path: 'rapports-accident', component: AccidentReportsListComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'accidents' } },
  // Calypso 9 — the id-less /rapport-accident used to render a hardcoded
  // demo scenario (the 2026-04-14 Jemmal rollover), which the navbar linked
  // to → every "report" looked identical. Redirect it to the real list so
  // the static demo page is unreachable. A report is ALWAYS opened with an
  // id (from the list row or a notification deep-link).
  { path: 'rapport-accident', redirectTo: '/accident-reports', pathMatch: 'full' },
  { path: 'rapport-accident/:accidentId', component: AccidentReportComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'accidents' } },
  
  // Fleet management module
  { path: 'fleet-management', component: FleetManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'fleet_management' } },
  { path: 'gestion-flotte', component: FleetManagementComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'fleet_management' } },
  
  // Emprunts véhicules (location companies only)
  { path: 'emprunts', component: VehicleLoansComponent, canActivate: [AuthGuard, FeatureGuard, LocationCompanyGuard], data: { feature: 'fleet_management' } },

  // Tours module
  { path: 'tours', component: ToursComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'tours' } },
  { path: 'tournees', component: ToursComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'tours' } },
  
  // Repairs module (part of maintenance)
  { path: 'repairs', component: RepairsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  { path: 'reparations', component: RepairsComponent, canActivate: [AuthGuard, FeatureGuard], data: { feature: 'maintenance' } },
  
  // Admin routes (login public, rest requires system admin)
  { path: 'admin', children: adminRoutes },
  
  // Fallback
  { path: '**', redirectTo: '' },
];
