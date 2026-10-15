using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicleDeletionPreview;

/// <summary>
/// Ce que la suppression d'un véhicule effacera avec lui, pour la fenêtre de confirmation de
/// l'écran Véhicules (Calypso GPA, 15/10/2026), en chiffres avant de confirmer. Même partage
/// que <see cref="Common.Helpers.VehicleDeletionHelper"/> : les lignes à clé NOT NULL sont
/// effacées (dépenses, entretiens, réparations…) ; les pleins (vehicle_id nullable) et les
/// dossiers de sinistre (sans clé) restent enregistrés, détachés du véhicule, avec sa plaque.
/// </summary>
public record GetVehicleDeletionPreviewQuery(int Id) : IQuery<VehicleDeletionPreviewDto>;

public record VehicleDeletionPreviewDto(
    int Id, string Name, string? Plate,
    IReadOnlyList<VehicleHistoryCountDto> History,
    IReadOnlyList<VehicleHistoryCountDto> Kept);

/// <summary>Une ligne d'historique, libellé déjà accordé au nombre (« 1 plein », « 42 pleins »).</summary>
public record VehicleHistoryCountDto(int Count, string Label);

public class GetVehicleDeletionPreviewQueryHandler : IRequestHandler<GetVehicleDeletionPreviewQuery, VehicleDeletionPreviewDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetVehicleDeletionPreviewQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<VehicleDeletionPreviewDto> Handle(GetVehicleDeletionPreviewQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        var id = request.Id;

        var vehicle = await _context.Vehicles.AsNoTracking()
            .Where(v => v.Id == id && v.CompanyId == companyId)
            .Select(v => new { v.Name, v.Plate })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Vehicle", id);

        await VehicleWriteRules.EnsureClientManagesFleetAsync(
            _context, _tenant, companyId, VehicleWriteRules.GpsFleetDeleteMessage, ct);

        // Un comptage après l'autre : le DbContext ne se partage pas entre requêtes parallèles.
        var history = new List<VehicleHistoryCountDto>();
        var kept = new List<VehicleHistoryCountDto>();
        static void AddTo(List<VehicleHistoryCountDto> list, int count, string singulier, string pluriel)
        {
            if (count > 0) list.Add(new VehicleHistoryCountDto(count, count == 1 ? singulier : pluriel));
        }
        void Add(int count, string singulier, string pluriel) => AddTo(history, count, singulier, pluriel);

        Add(await _context.VehicleCosts.CountAsync(c => c.VehicleId == id, ct), "dépense", "dépenses");
        Add(await _context.MaintenanceLogs.CountAsync(m => m.VehicleId == id, ct)
            + await _context.MaintenanceRecords.CountAsync(m => m.VehicleId == id, ct),
            "entretien réalisé", "entretiens réalisés");
        Add(await _context.Repairs.CountAsync(r => r.VehicleId == id, ct), "réparation", "réparations");
        Add(await _context.VehicleDocuments.CountAsync(d => d.VehicleId == id, ct), "document", "documents");
        Add(await _context.AcquisitionPayments.CountAsync(p => p.VehicleId == id, ct),
            "échéance de crédit ou de leasing", "échéances de crédit ou de leasing");
        Add(await _context.Reservations.CountAsync(r => r.VehicleId == id, ct), "réservation", "réservations");
        Add(await _context.Trips.CountAsync(t => t.VehicleId == id, ct), "trajet", "trajets");

        // Conservés, détachés du véhicule : ils gardent sa plaque et restent dans Carburant et Sinistres.
        AddTo(kept, await _context.FuelEntries.CountAsync(f => f.VehicleId == id, ct), "plein", "pleins");
        AddTo(kept, await _context.AccidentEvents.CountAsync(a => a.VehicleId == id, ct), "sinistre", "sinistres");

        return new VehicleDeletionPreviewDto(id, vehicle.Name, vehicle.Plate, history, kept);
    }
}
