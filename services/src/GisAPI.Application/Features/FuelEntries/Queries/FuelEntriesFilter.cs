using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Queries;

/// <summary>
/// Périmètre et filtres de l'écran Carburant, communs à la liste des pleins et à ses totaux :
/// les tuiles doivent porter sur EXACTEMENT les pleins que la liste pagine. Elles étaient
/// calculées sur les 100 lignes chargées (« 8 004 » pour 439 pleins et 42 383 € en base,
/// campagne de tests GPA du 06/10/2026, erreur n°3).
/// </summary>
internal static class FuelEntriesFilter
{
    public static async Task<IQueryable<FuelEntry>> AppliquerAsync(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        int? fuelTypeId,
        string? vehiclePlate,
        DateTime? startDate,
        DateTime? endDate,
        int? vehicleId,
        CancellationToken cancellationToken)
    {
        var companyId = tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        // Portee vehicules : chaque plein expose le montant, la station, le numero de
        // facture et le NOM DU CHAUFFEUR. Fuite constatee : un employe restreint a ses
        // vehicules voyait les pleins de tout le parc. Seul l'admin societe voit tout.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(context, tenantService, cancellationToken);

        var query = context.FuelEntries
            .Where(e => e.CompanyId == companyId)
            .AsQueryable();

        if (scope is not null)
        {
            // Les saisies non rattachees a un vehicule (VehicleId null) ne restent visibles
            // que si leur plaque correspond a un vehicule autorise — meme regle de
            // rattachement que les rapports carburant (audit / comparatif).
            var scopedPlates = await context.Vehicles
                .AsNoTracking()
                .Where(v => v.CompanyId == companyId
                            && scope.Contains(v.Id)
                            && v.Plate != null && v.Plate != "")
                .Select(v => v.Plate!)
                .ToListAsync(cancellationToken);

            query = query.Where(e =>
                (e.VehicleId != null && scope.Contains(e.VehicleId.Value))
                || (e.VehicleId == null && scopedPlates.Contains(e.VehiclePlate)));
        }

        if (fuelTypeId.HasValue)
            query = query.Where(e => e.FuelTypeId == fuelTypeId.Value);

        if (vehicleId.HasValue)
            query = query.Where(e => e.VehicleId == vehicleId.Value);

        if (!string.IsNullOrEmpty(vehiclePlate))
            query = query.Where(e => e.VehiclePlate.Contains(vehiclePlate));

        if (startDate.HasValue)
        {
            var debut = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
            query = query.Where(e => e.InvoiceDate >= debut);
        }

        if (endDate.HasValue)
        {
            var fin = DateTime.SpecifyKind(endDate.Value.AddDays(1), DateTimeKind.Utc);
            query = query.Where(e => e.InvoiceDate < fin);
        }

        return query;
    }
}
