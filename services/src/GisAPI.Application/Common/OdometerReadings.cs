using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common;

/// <summary>
/// Relevés compteur SAISIS, rassemblés depuis tous les écrans où le client en
/// note un. Alimente <see cref="OdometerDistance"/> pour les véhicules qui n'ont
/// pas de boîtier GPS remontant l'odomètre.
///
/// Pourquoi quatre sources et non une (recette client du 10/09/2026) : le
/// compteur du boîtier n'existe que sur l'offre Calypso GPS, et seulement sur
/// les boîtiers branchés au bus CAN. Partout ailleurs — offre Calypso GPA,
/// véhicule sans boîtier, boîtier sans CAN — le kilométrage se reconstitue à
/// partir de ce que le client saisit : un plein, un entretien, une réparation,
/// une dépense. Les rapports ne lisaient jusqu'ici que les pleins (rapports de
/// coûts du 04/09) ou uniquement les trames GPS (rapport « Coûts mensuel par
/// véhicule »), et affichaient donc 0 km à un client qui avait pourtant noté son
/// compteur à chaque passage à l'atelier.
///
/// Mesure sur le jeu de recette (société 7, année 2026) : 399 pleins, 36
/// entretiens, 37 réparations et 36 dépenses portent tous un relevé. Ne lire que
/// les pleins revenait à ignorer 109 relevés sur 508.
///
/// La fiche véhicule (<c>vehicles.mileage</c>) n'est délibérément PAS une source
/// ici : elle porte le compteur COURANT, sans date ni historique, donc elle ne
/// permet aucun calcul de distance sur une période. Elle sert de repli
/// d'affichage là où c'est le compteur, et non la distance, qui est demandé.
/// </summary>
public static class OdometerReadings
{
    /// <summary>
    /// Charge les relevés de tous les véhicules demandés sur la période, en
    /// quatre requêtes (une par table), et les rend groupés par véhicule.
    /// Les relevés nuls ou à zéro sont écartés ici : <see cref="OdometerDistance"/>
    /// les écarterait de toute façon, autant ne pas les transporter.
    /// </summary>
    /// <param name="startUtc">Début inclus.</param>
    /// <param name="endExclusiveUtc">Fin EXCLUE : une borne au jour ferait
    /// disparaître les saisies du dernier jour de la période.</param>
    public static async Task<ILookup<int, (long Km, DateTime Date)>> LoadAsync(
        IGisDbContext context,
        int companyId,
        IReadOnlyCollection<int> vehicleIds,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        CancellationToken ct)
    {
        if (vehicleIds.Count == 0)
            return Array.Empty<(int VehicleId, long Km, DateTime Date)>()
                .ToLookup(r => r.VehicleId, r => (r.Km, r.Date));

        var ids = vehicleIds as ICollection<int> ?? vehicleIds.ToList();

        // ── Pleins ────────────────────────────────────────────────────────────
        var fromFuel = await context.FuelEntries.AsNoTracking()
            .Where(f => f.VehicleId.HasValue
                     && ids.Contains(f.VehicleId.Value)
                     && f.OdometerKm.HasValue
                     && f.OdometerKm.Value > 0
                     && f.InvoiceDate >= startUtc
                     && f.InvoiceDate < endExclusiveUtc)
            .Select(f => new { VehicleId = f.VehicleId!.Value, Km = f.OdometerKm!.Value, Date = f.InvoiceDate })
            .ToListAsync(ct);

        // ── Entretiens réalisés ───────────────────────────────────────────────
        var fromMaintenance = await context.MaintenanceLogs.AsNoTracking()
            .Where(m => m.CompanyId == companyId
                     && ids.Contains(m.VehicleId)
                     && m.DoneKm > 0
                     && m.DoneDate >= startUtc
                     && m.DoneDate < endExclusiveUtc)
            .Select(m => new { m.VehicleId, Km = (long)m.DoneKm, Date = m.DoneDate })
            .ToListAsync(ct);

        // ── Réparations (les annulées ne décrivent aucun passage à l'atelier) ──
        var fromRepairs = await context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId
                     && ids.Contains(r.VehicleId)
                     && r.MileageAtRepair.HasValue
                     && r.MileageAtRepair.Value > 0
                     && r.Status != "cancelled"
                     && r.RepairDate >= startUtc
                     && r.RepairDate < endExclusiveUtc)
            .Select(r => new { r.VehicleId, Km = (long)r.MileageAtRepair!.Value, Date = r.RepairDate })
            .ToListAsync(ct);

        // ── Dépenses véhicule (assurance, vignette, péage… avec compteur) ─────
        var fromCosts = await context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && ids.Contains(c.VehicleId)
                     && c.Mileage.HasValue
                     && c.Mileage.Value > 0
                     && c.Date >= startUtc
                     && c.Date < endExclusiveUtc)
            .Select(c => new { c.VehicleId, Km = (long)c.Mileage!.Value, Date = c.Date })
            .ToListAsync(ct);

        return fromFuel
            .Concat(fromMaintenance)
            .Concat(fromRepairs)
            .Concat(fromCosts)
            .ToLookup(r => r.VehicleId, r => (r.Km, r.Date));
    }
}
