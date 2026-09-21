using System.Security.Claims;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Middleware;

public class PermissionMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Marque « aucune case de module exigée » dans <see cref="_modulePermissions"/> : le chemin
    /// reste soumis à tous les contrôles de société (jeton d'un utilisateur supprimé refusé,
    /// société courante chargée), mais aucun droit de module n'est demandé.
    ///
    /// <para>Une entrée EXPLICITE plutôt qu'une absence de clé : sans elle, c'est le préfixe
    /// parent qui reprend la main (« /api/costs » impose CanCosts à tout ce qui est rangé
    /// dessous). Elle n'est pas non plus rangée dans <c>_skipRoutes</c>, qui rendrait la main
    /// AVANT le chargement de l'utilisateur — le jeton encore valide d'un compte supprimé
    /// pourrait alors consommer le quota d'IA de la société.</para>
    /// </summary>
    internal const string PermissionNonRequise = "AucunePermissionDeModule";

    // Map API route prefixes to the required user permission field
    private static readonly Dictionary<string, string> _modulePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/users", "CanUsers" },
        { "/api/roles", "CanUsers" },
        { "/api/drivers", "CanDrivers" },
        { "/api/employees", "CanDrivers" },
        // Per-report user permissions (specific routes before generic /api/reports)
        { "/api/reports/trips", "CanReportTrips" },
        { "/api/reports/fuel", "CanReportFuel" },
        { "/api/reports/speed-infraction", "CanReportSpeedInfraction" },
        { "/api/reports/speed", "CanReportSpeed" },
        { "/api/reports/stops", "CanReportStops" },
        { "/api/reports/mileage-period", "CanReportMileagePeriod" },
        { "/api/reports/mileage", "CanReportMileage" },
        { "/api/reports/costs", "CanReportCosts" },
        // Rapports de coûts (04/09/2026) : une case par rapport depuis la recette du
        // 11/09/2026 (migration 046) — ils n'héritent plus de « Réparations véhicules ».
        { "/api/reports/costs/operating", "CanReportOperatingCost" },
        { "/api/reports/costs/evolution", "CanReportCostEvolution" },
        { "/api/reports/costs/ranking", "CanReportCostRanking" },
        { "/api/reports/costs/repair-frequency", "CanReportRepairFrequency" },
        { "/api/reports/maintenance", "CanReportMaintenance" },
        { "/api/reports/daily", "CanReportDaily" },
        { "/api/reports/monthly", "CanReportMonthly" },
        { "/api/reports/driving-behavior", "CanReportDrivingBehavior" },
        { "/api/reports/monthly-costs", "CanReportMonthlyCosts" },
        // Même action que monthly-costs, route dédiée pour porter sa propre case (11/09/2026).
        { "/api/reports/monthly-fuel", "CanReportMonthlyFuel" },
        // Generic reports fallback (list, create, schedules)
        { "/api/reports", "CanReports" },
        // Rapport IA Flotte : vit dans l'écran Rapports, n'avait aucune case par utilisateur.
        { "/api/ai-chat/fleet-report", "CanReportAiFleet" },
        { "/api/geofences", "CanGeofences" },
        // Les clés doivent reprendre le [Route] EXACT du contrôleur (le chemin n'est que mis en
        // minuscules). « /api/vehiclemaintenance » (sans tiret) ne correspondait à aucune route :
        // tout VehicleMaintenanceController, dont mark-done qui crée une dépense et relève le
        // kilométrage, échappait à ce contrôle et à celui d'abonnement. MaintenanceSchedulerController
        // est routé sous /api/maintenance, déjà couvert.
        { "/api/maintenance", "CanMaintenance" },
        { "/api/maintenance-templates", "CanMaintenance" },
        { "/api/vehicle-maintenance", "CanMaintenance" },
        // Journaux d'entretien de toute la flotte : seule source du rapport « Maintenance »
        // (reports.component), que le front ouvre sur canReportMaintenance et non canMaintenance.
        { "/api/vehicle-maintenance/logs", "CanReportMaintenance" },
        // Scan de facture IA (décision de Karim du 19/09/2026) : ces deux actions ne relèvent
        // PAS du module Dépenses. Elles n'enregistrent rien — elles rendent les champs LUS sur
        // un document, que l'écran appelant (Carburant, Entretien effectué, Nouvelle réparation,
        // Échéances, Dépenses) remplit ensuite à sa façon, sous SON propre droit. Ce qui gouverne
        // le scan est le QUOTA DE LA SOCIÉTÉ (societes.InvoiceScanMonthlyLimit, 0 = désactivé),
        // contrôlé dans CostsController. Sans ces deux clés, le préfixe « /api/costs » ci-dessous
        // imposait CanCosts ET ModuleCosts : un client abonné à Carburant mais pas à Dépenses, ou
        // un utilisateur sans la case Dépenses, voyait le bouton sur ces quatre écrans et se
        // faisait refuser avec un message parlant d'un AUTRE module. Clés PLUS LONGUES que
        // « /api/costs », donc retenues par MostSpecificRuleKey — et appariées à la frontière
        // de segment, parce qu'elles OUVRENT (voir _clesOuvrantes).
        { "/api/costs/scan-quota", PermissionNonRequise },
        { "/api/costs/scan-invoice", PermissionNonRequise },
        { "/api/costs", "CanCosts" },
        // Échéances d'acquisition (07/09/2026) : lignes de l'écran Dépenses, mêmes droits que /api/costs.
        { "/api/acquisition-payments", "CanCosts" },
        { "/api/fuelentries", "CanFuel" },
        // « Estimation coûts carburant » et « Carburant réel vs GPS » : rapports de l'écran
        // Rapports (seuls appelants de ces trois routes), une case chacun depuis le 11/09/2026.
        { "/api/fuelexpenses/statistics", "CanReportFuelEstimation" },
        { "/api/fuelexpenses/comparison", "CanReportFuelComparison" },
        { "/api/fuelexpenses/vehicle-audit", "CanReportFuelComparison" },
        { "/api/fuelexpenses", "CanFuel" },
        { "/api/fuelrecords", "CanFuel" },
        { "/api/documents", "CanDocuments" },
        // AccidentClaimsController a été remplacé par AccidentReportsController, routé
        // « api/accident-reports » : la clé « /api/accidentclaims » ne correspondait plus à
        // AUCUN chemin réel et tout le module Sinistres échappait aux deux contrôles — même
        // panne que « /api/fleetmanagement » le 16/09/2026. L'ancienne clé est conservée :
        // elle ne coûte rien et protège un éventuel appelant resté sur l'ancien chemin.
        { "/api/accident-reports", "CanAccidents" },
        { "/api/accidentclaims", "CanAccidents" },
        { "/api/suppliers", "CanSuppliers" },
        // FleetManagementController est routé « api/fleet » : la clé « /api/fleetmanagement »
        // ne correspondait à AUCUN chemin réel, et tout le module échappait aux deux contrôles
        // (recette du 16/09/2026). « /api/fleet » couvre aussi l'ancienne clé.
        { "/api/fleet", "CanFleetManagement" },
        { "/api/tours", "CanTours" },
        { "/api/gps", "CanMonitoring" },
        { "/api/gpsdevices", "CanMonitoring" },
        { "/api/trips", "CanMonitoring" },
        { "/api/vehiclestops", "CanMonitoring" },
        { "/api/alerts", "CanMonitoring" },
        { "/api/drivingbehavior", "CanMonitoring" },
        { "/api/vehicles/with-positions", "CanMonitoring" },
        { "/api/vehicles", "CanVehicles" },
        { "/api/vehicleassignments", "CanVehicles" },
    };

    // Map API route prefixes to the required subscription module flag
    // NOTE: More specific routes (e.g. /api/reports/trips) must appear before
    //       their parent (e.g. /api/reports) because matching uses FirstOrDefault.
    private static readonly Dictionary<string, Func<SubscriptionType, bool>> _subscriptionModuleChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/gps", sub => sub.ModuleMonitoring },
        { "/api/gpsdevices", sub => sub.ModuleMonitoring },
        { "/api/trips", sub => sub.ModuleMonitoring },
        { "/api/vehiclestops", sub => sub.ModuleMonitoring },
        { "/api/alerts", sub => sub.ModuleMonitoring },
        { "/api/drivingbehavior", sub => sub.DrivingBehavior },
        { "/api/geofences", sub => sub.ModuleGeofences },
        // Report-level subscription checks (specific report sub-routes)
        { "/api/reports/trips", sub => sub.ModuleReports && sub.ReportTrips },
        { "/api/reports/fuel", sub => sub.ModuleReports && sub.ReportFuel },
        { "/api/reports/speed", sub => sub.ModuleReports && sub.ReportSpeed },
        { "/api/reports/stops", sub => sub.ModuleReports && sub.ReportStops },
        { "/api/reports/mileage-period", sub => sub.ModuleReports && sub.ReportMileagePeriod },
        { "/api/reports/mileage", sub => sub.ModuleReports && sub.ReportMileage },
        { "/api/reports/costs", sub => sub.ModuleReports && sub.ReportCosts },
        // Rapports de coûts (04/09/2026) — déjà couverts par le préfixe ci-dessus, déclarés pour la lisibilité.
        { "/api/reports/costs/operating", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/evolution", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/ranking", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/repair-frequency", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/maintenance", sub => sub.ModuleReports && sub.ReportMaintenance },
        { "/api/reports/daily", sub => sub.ModuleReports && sub.ReportDaily },
        { "/api/reports/monthly", sub => sub.ModuleReports && sub.ReportMonthly },
        { "/api/reports/speed-infraction", sub => sub.ModuleReports && sub.ReportSpeedInfraction },
        { "/api/reports/driving-behavior", sub => sub.ModuleReports && sub.ReportDrivingBehavior },
        { "/api/reports/monthly-costs", sub => sub.ModuleReports && sub.ReportMonthlyCosts },
        { "/api/reports/monthly-fuel", sub => sub.ModuleReports && sub.ReportMonthlyCosts },
        // Generic /api/reports fallback (list reports, create, schedules, etc.)
        { "/api/reports", sub => sub.ModuleReports },
        // Rapport IA Flotte : même drapeau d'abonnement que l'écran (advancedReports).
        { "/api/ai-chat/fleet-report", sub => sub.ModuleReports && sub.AdvancedReports },
        // Mêmes clés que _modulePermissions ci-dessus (tirets compris), pour la même raison.
        { "/api/maintenance", sub => sub.ModuleMaintenance },
        { "/api/maintenance-templates", sub => sub.ModuleMaintenance },
        { "/api/vehicle-maintenance", sub => sub.ModuleMaintenance },
        { "/api/vehicle-maintenance/logs", sub => sub.ModuleReports && sub.ReportMaintenance },
        // Scan de facture IA : même raison que dans _modulePermissions ci-dessus — c'est le quota
        // mensuel de la société qui ouvre ou ferme la fonction, pas l'abonnement Dépenses. Le
        // refus « fonction non activée » est rendu par CostsController, avec le bon message.
        { "/api/costs/scan-quota", _ => true },
        { "/api/costs/scan-invoice", _ => true },
        { "/api/costs", sub => sub.ModuleCosts },
        // Échéances d'acquisition (07/09/2026) : lignes de l'écran Dépenses, même module que /api/costs.
        { "/api/acquisition-payments", sub => sub.ModuleCosts },
        { "/api/fuelentries", sub => sub.ModuleFuel },
        // Ces deux endpoints DÉDUISENT la consommation des positions GPS
        // (FuelCalculationService lit GpsPositions et Trips). Sans boîtier la
        // distance vaut 0 et le résultat est une estimation fabriquée, doublée
        // d'un verdict « anti-fraude » qui classe toute la flotte en suspect.
        // Ils relèvent donc de ReportFuel, pas du simple module Carburant — qui
        // reste ouvert pour la saisie manuelle des pleins.
        // Placés AVANT le préfixe générique : la correspondance retient la clé
        // la plus longue, mais on les déclare ici pour que l'intention soit lisible.
        { "/api/fuelexpenses/statistics", sub => sub.ModuleFuel && sub.ReportFuel },
        { "/api/fuelexpenses/comparison", sub => sub.ModuleFuel && sub.ReportFuel },
        { "/api/fuelexpenses/vehicle-audit", sub => sub.ModuleFuel && sub.ReportFuel },
        // /api/fuelexpenses/real-consumption reste volontairement sur ModuleFuel :
        // c'est le calcul plein-à-plein, purement manuel, écrit pour les clients
        // sans boîtier. Il ne doit PAS dépendre de ReportFuel.
        { "/api/fuelexpenses", sub => sub.ModuleFuel },
        { "/api/fuelrecords", sub => sub.ModuleFuel },
        { "/api/documents", sub => sub.ModuleDocuments },
        // Même correction que dans _modulePermissions : le [Route] EXACT du contrôleur.
        { "/api/accident-reports", sub => sub.ModuleAccidents },
        { "/api/accidentclaims", sub => sub.ModuleAccidents },
        { "/api/suppliers", sub => sub.ModuleSuppliers },
        // Même clé que _modulePermissions ci-dessus : le [Route] EXACT du contrôleur.
        { "/api/fleet", sub => sub.ModuleFleetManagement },
        { "/api/tours", sub => sub.ModuleTours },
        { "/api/users", sub => sub.ModuleUsers },
        { "/api/roles", sub => sub.ModuleUsers },
        { "/api/employees", sub => sub.ModuleEmployees },
        { "/api/drivers", sub => sub.ModuleEmployees },
        { "/api/vehicles/with-positions", sub => sub.ModuleMonitoring },
        { "/api/vehicles", sub => sub.ModuleVehicles },
        { "/api/vehicleassignments", sub => sub.ModuleVehicles },
    };

    // Routes that skip all permission/subscription checks (always accessible when authenticated)
    // /api/brands n'y porte plus que des lectures (référentiel global) : ses mutations sont
    // sous /api/admin/brands, donc derrière la garde administrateur système ci-dessous.
    private static readonly HashSet<string> _skipRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth",
        "/api/dashboard",
        "/api/notifications",
        "/api/settings",
        "/api/profile",
        "/api/subscription",
        "/api/brands",
        "/api/fuelprices",
        "/api/parts",
        "/api/poi",
        "/api/routing",
        "/api/statistics",
    };

    /// <summary>
    /// Clés des deux tables ci-dessus qui OUVRENT un accès au lieu de le restreindre :
    /// aujourd'hui les deux routes du scan de facture IA, qui retirent la case Dépenses
    /// et l'abonnement Dépenses à ce qui vit sous « /api/costs ».
    ///
    /// <para>Elles s'apparient à la FRONTIÈRE DE SEGMENT, jamais par simple préfixe : c'est
    /// la leçon de la recette du 16/09/2026, écrite plus bas dans <see cref="IsRouteOrSubRoute"/>
    /// — « /api/vehiclestops » commence par « /api/vehicles » sans en être une sous-route.
    /// Sans cela, une route future « /api/costs/scan-quota-detaillee » hériterait de
    /// l'ouverture sans que personne ne l'ait décidé ; avec, elle retombe sur « /api/costs »
    /// et reste gardée. Les clés qui RESTREIGNENT gardent le préfixe : il ne peut qu'AJOUTER
    /// du contrôle, et le restreindre à la frontière de segment ouvrirait des sous-routes
    /// aujourd'hui gardées (« /api/costs/812 » n'est pas « /api/costs », mais doit bien
    /// exiger CanCosts).</para>
    /// </summary>
    private static readonly HashSet<string> _clesOuvrantes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/costs/scan-quota",
        "/api/costs/scan-invoice",
    };

    /// <summary>Cette clé ouvre-t-elle un accès (frontière de segment) ? (Interne pour les tests.)</summary>
    internal static bool EstCleOuvrante(string cle) => _clesOuvrantes.Contains(cle);

    /// <summary>
    /// Clés de <see cref="_modulePermissions"/> qui n'exigent aucune case de module : elles
    /// OUVRENT, donc elles doivent toutes figurer dans <see cref="_clesOuvrantes"/>. Exposé
    /// pour qu'un test tienne les deux listes ensemble. (Interne pour les tests.)
    /// </summary>
    internal static IEnumerable<string> ClesSansPermissionDeModule =>
        _modulePermissions.Where(e => e.Value == PermissionNonRequise).Select(e => e.Key);

    /// <summary>
    /// Clé la plus précise qui vise ce chemin dans une table de règles ; null si aucune.
    /// Une règle plus longue AFFINE celle du préfixe : le plus souvent elle ajoute du
    /// contrôle (un rapport exige sa propre case en plus du module Rapports), mais elle
    /// peut aussi en RETIRER — le scan de facture IA, rangé sous « /api/costs », n'exige
    /// ni la case Dépenses ni l'abonnement Dépenses. Dans les deux sens, c'est la clé la plus
    /// longue qui fait foi : une règle qui OUVRE doit donc être écrite en toutes lettres dans
    /// les deux tables, jamais laissée à l'absence de clé — et s'apparier à la frontière de
    /// segment (voir <see cref="_clesOuvrantes"/>).
    /// </summary>
    private static string? MostSpecificRuleKey(IEnumerable<string> keys, string path) =>
        keys
            .Where(k => Vise(k, path))
            .OrderByDescending(k => k.Length)
            .FirstOrDefault();

    /// <summary>Cette clé vise-t-elle ce chemin ? Préfixe pour une règle qui restreint,
    /// frontière de segment pour une règle qui ouvre.</summary>
    private static bool Vise(string cle, string chemin) =>
        EstCleOuvrante(cle)
            ? IsRouteOrSubRoute(chemin, cle)
            : chemin.StartsWith(cle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Permission utilisateur exigée par un chemin (préfixe le plus long) ; null si aucune.</summary>
    internal static string? RequiredUserPermission(string path) =>
        MostSpecificRuleKey(_modulePermissions.Keys, path) is { } key ? _modulePermissions[key] : null;

    /// <summary>
    /// L'utilisateur détient-il la permission nommée ? Un rapport exige le module Rapports
    /// ET sa propre case. Nom inconnu → accordé (la table ci-dessus fait foi).
    /// </summary>
    internal static bool IsGranted(GisAPI.Domain.Entities.User currentUser, string permission) => permission switch
    {
        // Ouverture VOULUE, écrite dans la table : voir PermissionNonRequise (scan de facture IA).
        // Déclarée avant le reste pour qu'elle ne se confonde pas avec le « nom inconnu » du bas.
        PermissionNonRequise => true,
        "CanMonitoring" => currentUser.CanMonitoring,
        "CanVehicles" => currentUser.CanVehicles,
        "CanUsers" => currentUser.CanUsers,
        "CanDrivers" => currentUser.CanDrivers,
        "CanReports" => currentUser.CanReports,
        "CanGeofences" => currentUser.CanGeofences,
        "CanMaintenance" => currentUser.CanMaintenance,
        "CanCosts" => currentUser.CanCosts,
        "CanFuel" => currentUser.CanFuel,
        "CanDocuments" => currentUser.CanDocuments,
        "CanAccidents" => currentUser.CanAccidents,
        "CanSuppliers" => currentUser.CanSuppliers,
        "CanFleetManagement" => currentUser.CanFleetManagement,
        "CanTours" => currentUser.CanTours,
        // Per-report permissions (require CanReports + specific report permission)
        "CanReportTrips" => currentUser.CanReports && currentUser.CanReportTrips,
        "CanReportFuel" => currentUser.CanReports && currentUser.CanReportFuel,
        "CanReportSpeed" => currentUser.CanReports && currentUser.CanReportSpeed,
        "CanReportStops" => currentUser.CanReports && currentUser.CanReportStops,
        "CanReportMileage" => currentUser.CanReports && currentUser.CanReportMileage,
        "CanReportCosts" => currentUser.CanReports && currentUser.CanReportCosts,
        "CanReportMaintenance" => currentUser.CanReports && currentUser.CanReportMaintenance,
        "CanReportDaily" => currentUser.CanReports && currentUser.CanReportDaily,
        "CanReportMonthly" => currentUser.CanReports && currentUser.CanReportMonthly,
        "CanReportMileagePeriod" => currentUser.CanReports && currentUser.CanReportMileagePeriod,
        "CanReportSpeedInfraction" => currentUser.CanReports && currentUser.CanReportSpeedInfraction,
        "CanReportDrivingBehavior" => currentUser.CanReports && currentUser.CanReportDrivingBehavior,
        "CanReportMonthlyCosts" => currentUser.CanReports && currentUser.CanReportMonthlyCosts,
        // Une case par rapport (recette du 11/09/2026, migration 046)
        "CanReportOperatingCost" => currentUser.CanReports && currentUser.CanReportOperatingCost,
        "CanReportCostEvolution" => currentUser.CanReports && currentUser.CanReportCostEvolution,
        "CanReportCostRanking" => currentUser.CanReports && currentUser.CanReportCostRanking,
        "CanReportRepairFrequency" => currentUser.CanReports && currentUser.CanReportRepairFrequency,
        "CanReportMonthlyFuel" => currentUser.CanReports && currentUser.CanReportMonthlyFuel,
        "CanReportAiFleet" => currentUser.CanReports && currentUser.CanReportAiFleet,
        "CanReportFuelEstimation" => currentUser.CanReports && currentUser.CanReportFuelEstimation,
        "CanReportFuelComparison" => currentUser.CanReports && currentUser.CanReportFuelComparison,
        _ => true
    };

    /// <summary>
    /// Routes du compte de l'appelant lui-même, ouvertes à tout utilisateur connecté (voir
    /// InvokeAsync) : lire son profil, changer son mot de passe (mot de passe actuel exigé),
    /// régler ses heures silencieuses. Chemins et méthodes EXACTS.
    /// </summary>
    internal static bool IsSelfServiceUserRoute(string path, string method) =>
        (string.Equals(path, "/api/users/me", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method))
        || (HttpMethods.IsPut(method)
            && (string.Equals(path, "/api/users/me/password", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/api/users/me/quiet-hours", StringComparison.OrdinalIgnoreCase)));

    // Shared reference data routes: GET is always allowed (many modules need these),
    // but write operations (POST/PUT/DELETE) still require the specific permission.
    private static readonly HashSet<string> _readOnlySharedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/vehicles",
        "/api/drivers",
        "/api/employees",
        "/api/geofences",
    };

    /// <summary>
    /// Le chemin EST la route, ou l'une de ses sous-routes (frontière de segment).
    /// Une règle qui OUVRE un accès doit se comparer ainsi, jamais par simple préfixe :
    /// « /api/vehiclestops » commence par « /api/vehicles » sans en être une sous-route
    /// (recette du 16/09/2026). Les tables qui RESTREIGNENT gardent le préfixe le plus
    /// long, qui ne peut qu'ajouter du contrôle.
    /// </summary>
    internal static bool IsRouteOrSubRoute(string path, string route) =>
        string.Equals(path, route, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ce GET porte-t-il sur une donnée de référence partagée ? La frontière de segment ne suffit
    /// pas : « /api/vehicles/with-positions » EST une sous-route de « /api/vehicles » et héritait
    /// du passe-droit, si bien que sa propre règle Suivi (CanMonitoring) ne servait jamais
    /// (recette du 16/09/2026). La règle qui RESTREINT doit l'emporter sur celle qui OUVRE : le
    /// passe-droit n'est accordé que si aucune règle — permission ou abonnement — ne vise le
    /// chemin plus précisément que la route partagée elle-même.
    /// </summary>
    internal static bool IsSharedReferenceRead(string path, string method)
    {
        if (!HttpMethods.IsGet(method))
            return false;

        var sharedRoute = _readOnlySharedRoutes
            .Where(r => IsRouteOrSubRoute(path, r))
            .OrderByDescending(r => r.Length)
            .FirstOrDefault();

        if (sharedRoute == null)
            return false;

        return !EstPlusPrecise(MostSpecificRuleKey(_modulePermissions.Keys, path), sharedRoute)
            && !EstPlusPrecise(MostSpecificRuleKey(_subscriptionModuleChecks.Keys, path), sharedRoute);

        static bool EstPlusPrecise(string? cle, string route) => cle != null && cle.Length > route.Length;
    }

    public PermissionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Contrôle appliqué à une requête authentifiée, dans l'ordre exact d'InvokeAsync.</summary>
    internal enum RouteGate
    {
        /// <summary>Hors /api/ : aucun contrôle.</summary>
        NotApi,
        /// <summary>_skipRoutes : ouvert à tout utilisateur connecté.</summary>
        AlwaysOpen,
        /// <summary>Compte de l'appelant lui-même (IsSelfServiceUserRoute).</summary>
        SelfService,
        /// <summary>GET d'un référentiel partagé (IsSharedReferenceRead) : abonnement contrôlé, pas la permission utilisateur.</summary>
        SharedRead,
        /// <summary>/api/admin : administrateur système uniquement.</summary>
        SystemAdmin,
        /// <summary>Abonnement de la société puis permission de l'utilisateur.</summary>
        TenantChecks,
    }

    /// <summary>
    /// Aiguillage d'un chemin (déjà en minuscules) vers son contrôle. Extrait d'InvokeAsync
    /// pour qu'un test prouve qu'une route d'administration n'est court-circuitée par
    /// aucune exemption placée avant la garde système.
    /// </summary>
    internal static RouteGate ClassifyRoute(string path, string method)
    {
        if (!path.StartsWith("/api/"))
            return RouteGate.NotApi;

        if (_skipRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            return RouteGate.AlwaysOpen;

        // Routes « moi-même » : elles ne touchent que le compte de l'appelant. Rangées sous
        // /api/users, elles exigeaient CanUsers : un employé sans ce droit (utilisateur 51
        // de la recette du 11/09/2026) ne pouvait ni lire son propre profil ni régler ses
        // heures silencieuses. Liste EXACTE, pas de préfixe : /api/users/{id} et la gestion
        // des autres comptes restent soumis à CanUsers. PUT /api/users/me (nom, e-mail) N'EN
        // FAIT PAS PARTIE : changer son e-mail reste réservé à CanUsers — l'unicité de
        // l'e-mail n'y est vérifiée que dans la société et en respectant la casse.
        if (IsSelfServiceUserRoute(path, method))
            return RouteGate.SelfService;

        // Données de référence partagées (véhicules, chauffeurs, géofences) en lecture : frontière
        // de segment et règle plus précise prioritaire, voir IsSharedReferenceRead.
        if (IsSharedReferenceRead(path, method))
            return RouteGate.SharedRead;

        if (path.StartsWith("/api/admin"))
            return RouteGate.SystemAdmin;

        return RouteGate.TenantChecks;
    }

    // ── Compte chauffeur (application mobile, tournées) ──────────────────────────
    public const string DriverAppOnlyCode = "DRIVER_APP_ONLY";
    public const string DriverAppOnlyMessage =
        "Ce compte est réservé à l'application mobile Calypso, pour ses tournées.";
    public const string DriverAccountInactiveCode = "DRIVER_ACCOUNT_INACTIVE";
    /// <summary>
    /// Jeton émis AVANT le passage du compte en chauffeur : 401 (et non 403) pour que le
    /// client tente un rafraîchissement — refusé, la conversion a révoqué la session — puis
    /// déconnecte.
    /// </summary>
    public const string AccountTypeChangedCode = "ACCOUNT_TYPE_CHANGED";
    public const string AccountTypeChangedMessage =
        "Ce compte est devenu un compte chauffeur : reconnectez-vous depuis l'application mobile Calypso.";

    /// <summary>
    /// Les SEULS chemins qu'un compte chauffeur peut appeler : ses tournées
    /// (/api/driver-app), le rafraîchissement et la fermeture de sa session, et
    /// l'enregistrement du jeton de notification de son téléphone. Frontière de
    /// segment : « /api/driver-app » ne couvre pas « /api/driver-apple ».
    /// </summary>
    internal static readonly string[] DriverAppAllowedPrefixes =
    {
        "/api/driver-app",
        "/api/auth/refresh",
        "/api/auth/logout",
        "/api/devicetokens",
    };

    internal static bool IsDriverAppRoute(string path) =>
        DriverAppAllowedPrefixes.Any(p =>
            path.Equals(p, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

    // IGisDbContext plutôt que GisDbContext : la DI rend la même instance scopée
    // (DependencyInjection.cs), et le middleware devient testable sur TestGisDbContext.
    public async Task InvokeAsync(HttpContext context, IGisDbContext dbContext)
    {
        // Skip for non-authenticated requests
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value?.ToLower() ?? "";

        // ── Compte chauffeur (migration 050) : liste blanche stricte, AVANT tout aiguillage ──
        // Placé ici, et pas dans les contrôles par module plus bas, pour couvrir AUSSI ce que
        // ClassifyRoute laisse passer sans regarder : les deux hubs SignalR (/hubs/gps et
        // /api/hubs/gps — positions de toute la flotte), /health, les _skipRoutes (tableau de
        // bord, statistiques, notifications…) et les lectures partagées (liste des chauffeurs
        // avec CIN et téléphone). Un chauffeur ne voit que ses tournées.
        // La décision est RELUE EN BASE (statut + type de compte) à chaque appel : une
        // révocation prend effet tout de suite, sans attendre l'expiration du jeton (24 h).
        if (context.User.FindFirst(JwtClaims.AccountType)?.Value == UserAccountTypes.Driver)
        {
            if (!IsDriverAppRoute(path))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { message = DriverAppOnlyMessage, code = DriverAppOnlyCode });
                return;
            }

            var driverUid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;
            var compte = int.TryParse(driverUid, out var driverUserId)
                ? await dbContext.Users.IgnoreQueryFilters().AsNoTracking()
                    .Where(u => u.Id == driverUserId)
                    .Select(u => new { u.Status, u.AccountType })
                    .FirstOrDefaultAsync()
                : null;
            if (compte == null || compte.Status != "active" || compte.AccountType != UserAccountTypes.Driver)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Compte désactivé", code = DriverAccountInactiveCode });
                return;
            }

            await _next(context);
            return;
        }

        var gate = ClassifyRoute(path, context.Request.Method);

        // Hors API, routes toujours ouvertes, compte de l'appelant. La lecture d'un référentiel
        // partagé n'en fait PAS partie : elle passe encore le contrôle d'abonnement (CHECK 1).
        if (gate is RouteGate.NotApi or RouteGate.AlwaysOpen or RouteGate.SelfService)
        {
            await _next(context);
            return;
        }

        // Données de référence partagées (véhicules, chauffeurs, géofences) : leur LECTURE reste
        // ouverte à tout utilisateur connecté, beaucoup d'écrans en ont besoin. Mais ce passe-droit
        // ne porte QUE sur la permission utilisateur (CHECK 2) : il rendait aussi la main avant le
        // contrôle d'abonnement (CHECK 1), si bien qu'un module non vendu — Suivi, Géofences —
        // restait lisible par l'API, seule la garde Angular le masquait (recette du 16/09/2026).
        var isSharedReferenceRead = gate == RouteGate.SharedRead;

        // Admin routes check - only System Admin can access /api/admin/*
        if (gate == RouteGate.SystemAdmin)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var user = await dbContext.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                
                if (user == null || user.Role == null || !user.Role.IsSystemRole)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { message = "Accès réservé aux administrateurs système" });
                    return;
                }
            }
            await _next(context);
            return;
        }

        // ──────────────────────────────────────────────────────────
        // Load user with company + subscription (single query)
        // ──────────────────────────────────────────────────────────
        var uid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(uid, out var currentUserId))
        {
            await _next(context);
            return;
        }

        // Chargé pour toute requête NON EXEMPTÉE, même quand aucune règle ne vise le chemin : c'est
        // ce qui refuse (401) le jeton encore valide d'un utilisateur supprimé — mais SEULEMENT
        // ici. Les routes exemptées plus haut (hors /api dont le hub mobile /hubs/gps,
        // _skipRoutes comme /api/dashboard, /api/notifications, /api/settings,
        // /api/statistics, libre-service) ne passent jamais par ce chargement, pas plus
        // qu'une connexion SignalR déjà ouverte : un jeton d'utilisateur supprimé ou
        // désactivé y reste accepté jusqu'à son expiration (Jwt:ExpiryMinutes, 24 h par
        // défaut). Le refresh, lui, refuse un compte absent ou inactif.
        var currentUser = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Societe)
                .ThenInclude(s => s!.SubscriptionType)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        if (currentUser == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = "Utilisateur introuvable" });
            return;
        }

        // Compte passé « chauffeur » APRÈS l'émission de son jeton (pas de claim « acct ») :
        // la garde du haut ne l'a pas vu, la ligne en base tranche — dans les DEUX sens.
        // Ses propres routes lui sont ouvertes tout de suite (un salarié déjà connecté à
        // l'application, converti puis destinataire d'une tournée, recevait 403 sur SA
        // tournée jusqu'à l'expiration du jeton) ; tout le reste lui est fermé.
        // Fermé par un 401 ACCOUNT_TYPE_CHANGED, pas un 403 (relecture du 21/09/2026, R4c) :
        // le site et l'application 1.2 ne réagissent qu'au 401 — rafraîchissement, refusé
        // puisque la conversion a révoqué la session, puis déconnexion, ce qui coupe aussi
        // la connexion SignalR. Le 403 laissait la session du site ouverte jusqu'à
        // l'expiration du jeton (24 h), routes exemptées et hub déjà connecté compris.
        // (Le 403 DRIVER_APP_ONLY reste la réponse à un jeton « acct=driver », garde du haut.)
        if (currentUser.IsDriverAccount)
        {
            if (IsDriverAppRoute(path))
            {
                if (currentUser.Status == "active")
                {
                    await _next(context);
                    return;
                }
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Compte désactivé", code = DriverAccountInactiveCode });
                return;
            }
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = AccountTypeChangedMessage, code = AccountTypeChangedCode });
            return;
        }

        // System admins bypass all checks
        if (currentUser.Role?.IsSystemRole == true)
        {
            await _next(context);
            return;
        }

        // ──────────────────────────────────────────────────────────
        // CHECK 1: Subscription module access (company-level limit)
        // ──────────────────────────────────────────────────────────
        var subscriptionType = currentUser.Societe?.SubscriptionType;
        if (subscriptionType != null)
        {
            var subscriptionRuleKey = MostSpecificRuleKey(_subscriptionModuleChecks.Keys, path);
            if (subscriptionRuleKey != null)
            {
                var moduleEnabled = _subscriptionModuleChecks[subscriptionRuleKey](subscriptionType);
                if (!moduleEnabled)
                {
                    // Distinguish between a blocked report type vs a blocked module
                    var isReportTypeBlock = subscriptionRuleKey.StartsWith("/api/reports/", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(subscriptionRuleKey, "/api/ai-chat/fleet-report", StringComparison.OrdinalIgnoreCase);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = isReportTypeBlock
                            ? "Ce type de rapport n'est pas inclus dans votre abonnement"
                            : "Cette fonctionnalité n'est pas incluse dans votre abonnement",
                        code = isReportTypeBlock
                            ? "SUBSCRIPTION_REPORT_BLOCKED"
                            : "SUBSCRIPTION_MODULE_BLOCKED",
                        subscription = subscriptionType.Name
                    });
                    return;
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // CHECK 2: User-level permission (per-user module access)
        // ──────────────────────────────────────────────────────────
        var isAdmin = currentUser.Role?.IsCompanyAdmin == true || currentUser.AccessLevel == "admin";
        var requiredPermission = isSharedReferenceRead ? null : RequiredUserPermission(path);
        if (!isAdmin && requiredPermission != null && !IsGranted(currentUser, requiredPermission))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Vous n'avez pas accès à ce module",
                code = "USER_PERMISSION_DENIED"
            });
            return;
        }

        await _next(context);
    }
}

public static class PermissionMiddlewareExtensions
{
    public static IApplicationBuilder UsePermissionMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PermissionMiddleware>();
    }
}
