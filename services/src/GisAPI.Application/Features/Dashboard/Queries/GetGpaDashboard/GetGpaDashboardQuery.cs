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

public record GpaDashboardDto(
    DateTime From,                        // bornes appliquées : minuit UTC du 1er jour…
    DateTime To,                          // …et minuit UTC du dernier jour (inclus)
    GpaCostsDto Costs,
    GpaAcquisitionDto Acquisition,
    GpaLeasingRemainingDto LeasingRemaining,
    GpaInterventionsDto Interventions,
    GpaUpcomingMaintenanceDto UpcomingMaintenance,
    List<GpaAlertDto> Alerts,                // les plus urgentes, 20 au plus
    GpaAlertCountsDto AlertCounts,           // comptes AVANT la coupe à 20
    List<GpaMonthDto> Monthly,
    List<GpaTopVehicleDto> Top5,
    List<GpaInterventionDto> RecentInterventions);

/// <summary>Coûts d'exploitation de la période — définition du rapport « Coût d'exploitation » (acquisitions exclues).</summary>
public record GpaCostsDto(decimal Fuel, decimal Maintenance, decimal Repair, decimal Other, decimal Total);

/// <summary>
/// Coût complet d'acquisition du parc, indépendant de la période : achats
/// comptant, apports et toutes les mensualités (échues et à venir). Le nombre
/// de véhicules achetés comptant et financés (crédit/leasing) l'accompagne.
/// </summary>
public record GpaAcquisitionDto(decimal Total, int PurchasedVehicles, int FinancedVehicles);

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
