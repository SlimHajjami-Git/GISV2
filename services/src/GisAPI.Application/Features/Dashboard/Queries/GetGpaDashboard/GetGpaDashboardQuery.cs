using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;

/// <summary>
/// Tableau de bord d'un compte SANS GPS (offre Calypso GPA),
/// GET /api/dashboard/gpa?from=yyyy-MM-dd&amp;to=yyyy-MM-dd. Bornes en jours
/// entiers, jour de fin inclus. Toutes les valeurs viennent des données saisies
/// (dépenses, pleins, réparations, échéanciers, échéances) : aucune donnée GPS,
/// aucune estimation. Voir <see cref="GetGpaDashboardQueryHandler"/>.
/// </summary>
public record GetGpaDashboardQuery(DateTime From, DateTime To) : IQuery<GpaDashboardDto>;

// ── Contrat JSON (camelCase) — le front est écrit contre ces noms ───────────

/// <summary>
/// Un bloc à <c>null</c> = l'appelant n'a pas le droit de le voir
/// (<see cref="GpaSectionAccess"/>) : le front affiche « — » ou masque la carte.
/// /api/dashboard est exempté de PermissionMiddleware ; ce contrôle par bloc est
/// donc le SEUL qui s'applique à cette route.
/// </summary>
public record GpaDashboardDto(
    DateTime From,                        // bornes appliquées : minuit UTC du 1er jour…
    DateTime To,                          // …et minuit UTC du dernier jour (inclus)
    GpaCostsDto? Costs,
    GpaAcquisitionDto? Acquisition,
    GpaLeasingRemainingDto? LeasingRemaining,
    GpaInterventionsDto? Interventions,
    GpaUpcomingMaintenanceDto? UpcomingMaintenance,
    List<GpaAlertDto>? Alerts,                // les plus urgentes, 20 au plus
    GpaAlertCountsDto? AlertCounts,           // comptes AVANT la coupe à 20
    List<GpaMonthDto>? Monthly,
    List<GpaTopVehicleDto>? Top5,
    List<GpaInterventionDto>? RecentInterventions);

/// <summary>
/// Blocs du tableau de bord GPA visibles par l'appelant. Même définition
/// d'administrateur que PermissionMiddleware (rôle admin de société, niveau
/// d'accès « admin », rôle système) : il voit tout. Sinon chaque bloc suit la
/// case qui ouvre la même donnée ailleurs dans l'application :
/// <list type="bullet">
///   <item>coûts de la période / top 5 / 12 mois : « Dépenses » (CanCosts), ou le rapport
///     correspondant (Rapports + Coût d'exploitation / Classement / Évolution des coûts) ;</item>
///   <item>coût d'achats et reste à payer : « Dépenses » seulement (/api/acquisition-payments) ;</item>
///   <item>interventions (nombre et dernières, montants et fournisseurs) : Dépenses ou Entretien ;</item>
///   <item>échéances et alertes d'entretien : Entretien ; alertes de documents : Documents.</item>
/// </list>
/// Constat du 14/09/2026 : l'utilisateur de recette 51 (toutes ces cases à false)
/// recevait 403 sur chacune de ces routes, mais tous les montants par /api/dashboard/gpa.
/// </summary>
public sealed record GpaSectionAccess(
    bool Costs,
    bool Top5,
    bool Monthly,
    bool Acquisition,
    bool Interventions,
    bool Maintenance,
    bool Documents)
{
    public static GpaSectionAccess All { get; } = new(true, true, true, true, true, true, true);
    public static GpaSectionAccess None { get; } = new(false, false, false, false, false, false, false);

    /// <param name="user">Utilisateur appelant, rôle chargé ; null (introuvable) = rien.</param>
    /// <param name="isSystemAdmin">Administrateur système : tout, comme dans le middleware.</param>
    public static GpaSectionAccess For(GisAPI.Domain.Entities.User? user, bool isSystemAdmin)
    {
        if (isSystemAdmin) return All;
        if (user is null) return None;

        var isAdmin = user.Role?.IsSystemRole == true
                      || user.Role?.IsCompanyAdmin == true
                      || user.AccessLevel == "admin";
        if (isAdmin) return All;

        // Un rapport exige le module Rapports ET sa propre case (PermissionMiddleware.IsGranted).
        var reports = user.CanReports;
        return new GpaSectionAccess(
            Costs: user.CanCosts || (reports && user.CanReportOperatingCost),
            Top5: user.CanCosts || (reports && user.CanReportCostRanking),
            Monthly: user.CanCosts || (reports && user.CanReportCostEvolution),
            Acquisition: user.CanCosts,
            Interventions: user.CanCosts || user.CanMaintenance,
            Maintenance: user.CanMaintenance,
            Documents: user.CanDocuments);
    }
}

/// <summary>Coûts d'exploitation de la période — définition du rapport « Coût d'exploitation » (acquisitions exclues).</summary>
public record GpaCostsDto(decimal Fuel, decimal Maintenance, decimal Repair, decimal Other, decimal Total);

/// <summary>
/// Coût complet d'acquisition du parc, indépendant de la période : achats
/// comptant, apports et toutes les mensualités (échues et à venir). Le nombre
/// de véhicules achetés comptant et financés (crédit/leasing) l'accompagne.
/// <c>PeriodCost</c> = la part de la PÉRIODE choisie (échéances atteintes dans
/// [from, to]), la définition de la tuile « Achats véhicule » livrée le 11/09/2026
/// et du tableau de bord GPS : le coût complet ne suit pas la période, et sans
/// elle l'écran GPA ne montrait plus nulle part ce que la période a coûté en achats.
/// </summary>
public record GpaAcquisitionDto(decimal Total, int PurchasedVehicles, int FinancedVehicles, decimal PeriodCost);

/// <summary>Mensualités planifiées à venir (à la date du jour, indépendant de la période).</summary>
public record GpaLeasingRemainingDto(decimal Amount, int Contracts, int Installments);

/// <summary>Nombre d'interventions de la période (entretiens saisis + réparations non annulées).</summary>
public record GpaInterventionsDto(int Maintenance, int Repairs, int Total);

/// <summary>Échéances d'entretien à la date du jour (échéanciers non en pause, gabarit actif).</summary>
public record GpaUpcomingMaintenanceDto(int Next30Days, int Overdue);

/// <summary>
/// Alerte en direct : <c>kind</c> = maintenance | document ;
/// <c>severity</c> = critical | warning. Jamais d'alerte GPS.
/// </summary>
public record GpaAlertDto(
    string Kind,
    string Severity,
    string Title,
    string Detail,
    string? Plate,
    string VehicleName,
    DateTime? Date,                        // date d'échéance (null : échéance au km seulement)
    int? DaysLeft);                        // jours restants, négatif si dépassé

/// <summary>Nombre d'alertes en tout et de critiques, comptés avant la coupe de la liste.</summary>
public record GpaAlertCountsDto(int Total, int Critical);

public record GpaMonthDto(
    int Year,
    int Month,
    string Label,                          // « Oct. 2025 »
    decimal Fuel,
    decimal Maintenance,
    decimal Repair,
    decimal Other,
    decimal Total,
    bool IsPartial);                       // mois en cours, pas encore terminé

public record GpaTopVehicleDto(
    int VehicleId,
    string? Plate,
    string VehicleName,
    decimal Maintenance,
    decimal Repair,
    decimal Total);

/// <summary><c>kind</c> = entretien | reparation.</summary>
public record GpaInterventionDto(
    DateTime Date,
    string? Plate,
    string VehicleName,
    string Kind,
    string TypeLabel,
    string? Description,
    string? Supplier,
    int? MileageKm,
    decimal Cost);
