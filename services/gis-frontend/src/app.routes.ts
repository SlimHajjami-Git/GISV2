import { Routes } from '@angular/router';
import { environment } from './environments/environment';
import { AiLandingComponent } from './components/ai-landing.component';
import { LandingComponent } from './components/landing.component';
import { Fr2HomeComponent } from './components/france/fr2-home.component';
import { Fr2FeaturesComponent } from './components/france/fr2-features.component';
import { Fr2PricingComponent } from './components/france/fr2-pricing.component';
import { Fr2AutoComponent } from './components/france/fr2-auto.component';
import { FranceContactComponent } from './components/france/france-contact.component';
import { FrancePrivacyComponent, FranceLegalComponent, FranceRgpdComponent, FranceCookiesComponent } from './components/france/fr2-legal-pages.component';
import { TestAccueilComponent } from './components/france/test-accueil.component';
import { RegionGateComponent } from './components/region-gate.component';
import { LoginComponent } from './components/login.component';
import { ForgotPasswordComponent } from './components/forgot-password.component';
import { ResetPasswordComponent } from './components/reset-password.component';
import { RegisterComponent } from './components/register.component';
import { ConfirmEmailComponent } from './components/confirm-email.component';
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
import { SubscriptionBlockedComponent } from './components/subscription-blocked.component';
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


// The AI automobile assistant landing is PER-DEPLOYMENT: enabled only when the
// server's LOCAL environment.ts sets aiAssistantLanding: true (Calypso/TN).
// Deployments without the flag (e.g. Bougeo/DZ) get the classic marketing
// landing. `as any` on purpose: the flag may be absent from a deployment's
// local copy of environment.ts, and that absence must mean "disabled", not a
// compile error.
const aiLandingEnabled = (environment as any).aiAssistantLanding === true;

// L'INSCRIPTION LIBRE est elle aussi PAR DÉPLOIEMENT : activée seulement quand le
// environment.ts LOCAL du serveur pose selfSignup: true (Bougeo/DZ pour l'instant).
// Même `as any` et même raison : sur un déploiement dont la copie locale ignore la
// clé, son absence doit signifier « désactivée », pas une erreur de compilation.
//
// La route est INCLUSE CONDITIONNELLEMENT plutôt que gardée : là où la fonction
// n'est pas vendue, /inscription n'existe pas du tout. Le vrai verrou reste côté
// serveur (l'endpoint répond 404) — masquer un bouton ne ferme pas une API.
const selfSignupEnabled = (environment as any).selfSignup === true;

// Le MODULE ABONNEMENT est lui aussi PAR DÉPLOIEMENT : écran de gestion,
// entrée de menu et bandeau d'échéance. Certaines installations sont facturées
// hors application (Bougeo/DZ) et n'ont rien à y faire.
//
// Ici l'absence de clé vaut ACTIVÉ, à l'inverse des autres drapeaux : le module
// existe depuis toujours et la majorité des déploiements le veulent. Seul celui
// qui n'en veut pas pose `subscriptionModule: false` dans son environment.ts.
//
// L'écran /abonnement-suspendu, lui, reste TOUJOURS présent : c'est le filet
// qui explique la situation si un abonnement expire malgré tout.
const subscriptionModuleEnabled = (environment as any).subscriptionModule !== false;

// VITRINE EUROPÉENNE — décidée par le NOM DE DOMAINE (environment.europeanHostnames).
//
// Une première version se fiait au fuseau horaire du navigateur. C'était faux,
// et démontré en production : la Tunisie est à UTC+1 et le sélecteur Windows
// propose en tête « (UTC+01:00) Bruxelles, Copenhague, Madrid, Paris ». Un poste
// tunisien réglé ainsi se déclare Europe/Paris — les utilisateurs tunisiens
// recevaient la vitrine France. Le fuseau ne dit pas où est le visiteur, il dit
// comment sa machine a été réglée.
//
// La racine est aiguillée par RegionGateComponent : signaux immédiats
// (domaine, forçage, session) d'abord, puis pays de l'adresse IP via l'API
// (résolution locale côté serveur, sans tiers ni conservation). La table de
// routes reste statique — c'est le composant qui décide, pas la table.

export const routes: Routes = [
  // Public routes (no auth required)
  // When enabled, the AI automobile assistant is the first page users land on
  // (pre-login); "Accéder à Calypso" routes to /login and the former marketing
  // landing stays reachable at /accueil.
  { path: '', component: RegionGateComponent },
  // Le site commercial France : une coque (en-tête, pied de page, feuille de
  // style) et huit pages enfants, conformément au cahier des charges. Elles
  // vivent sous /fr et non à la racine pour qu'il n'existe qu'UNE adresse par
  // page : deux URL servant le même contenu se pénalisent mutuellement au
  // référencement, et le cahier des charges pose des exigences SEO précises.
  // Page temoin : reproduction de la capture ACCUEIL a comparer avant
  // adoption. Hors du site France, pour ne pas melanger les deux.
  { path: 'testacceuil', component: TestAccueilComponent },
  { path: 'testaccueil', redirectTo: 'testacceuil' },
  {
    path: 'fr',
    children: [
      { path: '', component: Fr2HomeComponent },
      { path: 'fonctionnalites', component: Fr2FeaturesComponent },
      { path: 'tarifs', component: Fr2PricingComponent },
      { path: 'calypso-auto', component: Fr2AutoComponent },
      { path: 'contact', component: FranceContactComponent },
      { path: 'confidentialite', component: FrancePrivacyComponent },
      { path: 'mentions-legales', component: FranceLegalComponent },
      { path: 'rgpd', component: FranceRgpdComponent },
      { path: 'cookies', component: FranceCookiesComponent },
      // Le document maitre range les « pages 404 » parmi les elements
      // interdits : une adresse inconnue sous /fr ramene a l accueil du site
      // plutot que d ouvrir une page supplementaire non demandee.
      { path: '**', redirectTo: '' }
    ]
  },
  { path: 'assistant', component: aiLandingEnabled ? AiLandingComponent : LandingComponent },
  { path: 'accueil', component: LandingComponent },
  { path: 'login', component: LoginComponent },
  // Reinitialisation de mot de passe. TOUJOURS presentes, meme la ou
  // l inscription libre est fermee : un utilisateur cree par un administrateur
  // peut lui aussi perdre son mot de passe, et n aurait alors aucun recours.
  { path: 'mot-de-passe-oublie', component: ForgotPasswordComponent },
  { path: 'reinitialiser-mot-de-passe', component: ResetPasswordComponent },
  ...(selfSignupEnabled
    ? [
        { path: 'inscription', component: RegisterComponent },
        { path: 'register', redirectTo: 'inscription', pathMatch: 'full' as const },
        // Cible du lien envoyé par email. Publique par nécessité : l'utilisateur
        // ne peut pas se connecter tant que son adresse n'est pas confirmée.
        { path: 'confirmation-email', component: ConfirmEmailComponent }
      ]
    : []),
  { path: 'device-check', component: DeviceCheckComponent },
  // Écran pleine page hors layout : abonnement de la société suspendu/expiré.
  { path: 'abonnement-suspendu', component: SubscriptionBlockedComponent },
  { path: 'politique-de-confidentialite', component: PrivacyPolicyComponent },
  { path: 'privacy-policy', redirectTo: 'politique-de-confidentialite', pathMatch: 'full' },
  // Cible du QR "Partager la position" du monitoring: tente d'ouvrir l'appli
  // mobile Calypso zoomée sur le véhicule, repli Google Maps. Publique.
  { path: 'l/vehicle/:id', loadComponent: () => import('./components/open-in-app.component').then(m => m.OpenInAppComponent) },
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
  ...(subscriptionModuleEnabled
    ? [{ path: 'subscription', component: SubscriptionComponent, canActivate: [AuthGuard] }]
    : []),
  
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
