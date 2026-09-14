using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Coûts d'acquisition lus sur l'échéancier PERSISTÉ (acquisition_payments),
/// avec repli sur le calcul à la volée d'<see cref="AcquisitionSchedule"/> pour
/// les véhicules qui n'ont encore AUCUNE ligne (transition sans backfill :
/// l'échéancier n'est généré qu'à la première ouverture de l'écran Dépenses).
///
/// <para>Définition partagée par le tableau de bord GPS
/// (<c>DashboardService.AcquisitionCostAsync</c>) et le tableau de bord GPA :
/// avant, elle ne vivait que dans la couche API et aurait dû être recopiée.</para>
///
/// <para>Lecture seule : JAMAIS de synchronisation ni de génération
/// d'échéancier ici. Les deux tableaux de bord tournent en lecture, et le GPS
/// est même pré-chauffé hors requête, sans contexte tenant.</para>
///
/// <para>Portée : <paramref name="scopeIds"/> suit la sémantique de
/// <c>VehicleScope</c> (null = tout le parc, liste vide = rien) et
/// <paramref name="vehicles"/> doit déjà être borné à la société ET à la
/// portée de l'appelant — c'est la liste du repli.</para>
/// </summary>
public static class AcquisitionCostCalculator
{
    /// <summary>
    /// Coût d'acquisition de la période [<paramref name="from"/>, <paramref name="to"/>]
    /// (jours inclus) : Σ (paid_amount ?? amount) des lignes qui COMPTENT
    /// (<see cref="AcquisitionPaymentRules"/> : payées, ou planifiées dont la date
    /// est atteinte — jamais les ignorées) et dont l'échéance tombe dans la
    /// période, en SQL ; PLUS <see cref="AcquisitionSchedule.Cost"/> pour les
    /// véhicules sans aucune ligne.
    /// </summary>
    public static async Task<decimal> PeriodCostAsync(
        IGisDbContext context,
        int companyId,
        List<int>? scopeIds,
        IEnumerable<Vehicle> vehicles,
        DateTime from,
        DateTime to,
        DateTime now,
        CancellationToken ct)
    {
        var persisted = Persisted(context, companyId, scopeIds);

        // La somme AVANT la liste des véhicules déjà synchronisés : si une
        // autre requête génère un échéancier entre les deux lectures, le
        // véhicule est alors compté par la liste (donc exclu du repli) sans
        // l'être par la somme — il manque une fois, au lieu d'être compté
        // deux fois. Une omission se corrige au calcul suivant ; un doublon
        // resterait affiché le temps du cache.
        var counted = await AcquisitionPaymentRules.CountedCostAsync(persisted, from, to, now, ct);
        var syncedIds = await SyncedVehicleIdsAsync(persisted, ct);

        return counted + AcquisitionSchedule.Cost(vehicles.Where(v => !syncedIds.Contains(v.Id)), from, to, now);
    }

    /// <summary>
    /// Reste à payer des contrats de leasing/crédit à la date du jour,
    /// indépendamment de toute période : les mensualités PLANIFIÉES dont
    /// l'échéance est STRICTEMENT future (une mensualité du jour est déjà une
    /// dépense, cf. <see cref="AcquisitionPaymentRules.IsCounted"/>). Les
    /// mensualités payées d'avance ou ignorées ne restent pas à payer ; l'apport
    /// n'est pas une mensualité.
    /// Repli, même règle que <see cref="PeriodCostAsync"/> : un véhicule
    /// <c>leasing</c> sans AUCUNE ligne compte ses échéances futures d'après
    /// <see cref="AcquisitionSchedule.LeasingDues"/> × mensualité.
    /// </summary>
    public static async Task<(decimal Amount, int Contracts, int Installments)> LeasingRemainingAsync(
        IGisDbContext context,
        int companyId,
        List<int>? scopeIds,
        IEnumerable<Vehicle> vehicles,
        DateTime now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.Date);
        var persisted = Persisted(context, companyId, scopeIds);

        // Même ordre de lecture que PeriodCostAsync (lignes, puis véhicules synchronisés).
        var future = await persisted
            .Where(p => p.Kind == AcquisitionPayment.Kinds.Mensualite
                        && p.Status == AcquisitionPayment.Statuses.Planned
                        && p.DueDate > today)
            .Select(p => new { p.VehicleId, p.Amount })
            .ToListAsync(ct);
        var syncedIds = await SyncedVehicleIdsAsync(persisted, ct);

        var amount = future.Sum(p => p.Amount);
        var installments = future.Count;
        var contracts = future.Select(p => p.VehicleId).ToHashSet();

        foreach (var v in vehicles.Where(v => !syncedIds.Contains(v.Id)))
        {
            // LeasingDues ne rend rien pour un véhicule qui n'est pas en leasing
            // ou dont le contrat est incomplet.
            var dues = AcquisitionSchedule.LeasingDues(v).Count(d => d.Due.Date > now.Date);
            if (dues == 0) continue;

            amount += dues * v.LeasingMonthlyPayment!.Value;
            installments += dues;
            contracts.Add(v.Id);
        }

        return (amount, contracts.Count, installments);
    }

    /// <summary>
    /// Coût COMPLET d'acquisition du parc, indépendant de toute période : prix
    /// d'achat comptant, apports et TOUTES les mensualités de crédit/leasing,
    /// échues comme à venir (recette du 11/09/2026 : le KPI « Coût d'achats » du
    /// tableau de bord GPA montre ce que le parc coûte, pas les seules échéances
    /// de la période). Une ligne ignorée ne compte pas ; montant = montant réglé
    /// s'il est connu, sinon montant prévu (<see cref="AcquisitionPaymentRules.CountedAmount"/>).
    /// Repli, même règle que <see cref="PeriodCostAsync"/> : un véhicule sans
    /// AUCUNE ligne compte son prix d'achat ou son apport daté, plus toutes les
    /// mensualités de son contrat (<see cref="AcquisitionSchedule.LeasingDues"/>).
    /// </summary>
    public static async Task<(decimal Amount, int PurchasedVehicles, int FinancedVehicles)> FleetTotalAsync(
        IGisDbContext context,
        int companyId,
        List<int>? scopeIds,
        IEnumerable<Vehicle> vehicles,
        CancellationToken ct)
    {
        var persisted = Persisted(context, companyId, scopeIds);

        // Même ordre de lecture que PeriodCostAsync (lignes, puis véhicules synchronisés).
        var lines = await persisted
            .Where(p => p.Status != AcquisitionPayment.Statuses.Skipped)
            .Select(p => new { p.VehicleId, p.Kind, p.Amount, p.PaidAmount })
            .ToListAsync(ct);
        var syncedIds = await SyncedVehicleIdsAsync(persisted, ct);

        var amount = lines.Sum(l => AcquisitionPaymentRules.CountedAmount(l.Amount, l.PaidAmount));
        var purchased = lines.Where(l => l.Kind == AcquisitionPayment.Kinds.Achat).Select(l => l.VehicleId).ToHashSet();
        var financed = lines.Where(l => l.Kind != AcquisitionPayment.Kinds.Achat).Select(l => l.VehicleId).ToHashSet();

        foreach (var v in vehicles.Where(v => !syncedIds.Contains(v.Id)))
        {
            var dues = AcquisitionSchedule.LeasingDues(v).Count();
            if (dues > 0)
            {
                amount += dues * v.LeasingMonthlyPayment!.Value;
                financed.Add(v.Id);
            }

            // Prix d'achat (comptant) ou apport (contrat), comme le générateur :
            // seulement s'il est daté.
            if (v.PurchasePrice > 0 && v.PurchaseDate is not null)
            {
                amount += v.PurchasePrice.Value;
                if (dues == 0) purchased.Add(v.Id);
            }
        }

        // Un véhicule passé du leasing à l'achat comptant garde ses mensualités
        // réglées : il est financé, pas compté deux fois.
        purchased.ExceptWith(financed);
        return (amount, purchased.Count, financed.Count);
    }

    /// <summary>Lignes de la société, bornées aux véhicules visibles (filtre société explicite).</summary>
    private static IQueryable<AcquisitionPayment> Persisted(IGisDbContext context, int companyId, List<int>? scopeIds)
    {
        var persisted = context.AcquisitionPayments.AsNoTracking()
            .Where(p => p.CompanyId == companyId);
        if (scopeIds != null)
            persisted = persisted.Where(p => scopeIds.Contains(p.VehicleId));
        return persisted;
    }

    private static async Task<HashSet<int>> SyncedVehicleIdsAsync(IQueryable<AcquisitionPayment> persisted, CancellationToken ct) =>
        (await persisted.Select(p => p.VehicleId).Distinct().ToListAsync(ct)).ToHashSet();
}
