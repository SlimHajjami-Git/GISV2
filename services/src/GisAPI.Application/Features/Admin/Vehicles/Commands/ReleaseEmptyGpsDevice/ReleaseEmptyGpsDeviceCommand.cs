using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.ReleaseEmptyGpsDevice;

/// <summary>
/// Libère (supprime) une fiche boîtier STRICTEMENT VIDE : jamais connectée, rattachée à aucun
/// véhicule, sans la moindre donnée. Porte de sortie proposée par la création de véhicule quand
/// une telle fiche bloque un MAT ou une SIM.
///
/// POURQUOI — recette du 09/10/2026 (Belive SAV, HTZ 344) : la suppression d'un véhicule
/// laisse sa fiche boîtier « non affectée » ; celle-ci, saisie avec un IMEI à 14 chiffres,
/// n'avait jamais rien reçu mais portait le MAT et la SIM que la re-création demandait. Aucun
/// écran ne permettait de s'en défaire. Tout le reste est refusé : une fiche qui a communiqué
/// ou qui porte un historique relève du remplacement de boîtier (GpsDeviceReplacementPlanner).
/// </summary>
/// <param name="PourSocieteId">
/// Société du véhicule en cours de création : la fiche doit lui appartenir (ou à la société par
/// défaut de l'ingestion) — une fiche pré-enregistrée chez un autre client ne se libère pas d'ici.
/// </param>
public record ReleaseEmptyGpsDeviceCommand(int DeviceId, int? PourSocieteId = null) : IRequest<ReleaseEmptyGpsDeviceResult>;

public record ReleaseEmptyGpsDeviceResult(bool Success, string Message, bool NotFound = false);

public class ReleaseEmptyGpsDeviceCommandHandler : IRequestHandler<ReleaseEmptyGpsDeviceCommand, ReleaseEmptyGpsDeviceResult>
{
    private readonly IGisDbContext _context;
    private readonly ILogger<ReleaseEmptyGpsDeviceCommandHandler> _logger;

    public ReleaseEmptyGpsDeviceCommandHandler(IGisDbContext context, ILogger<ReleaseEmptyGpsDeviceCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ReleaseEmptyGpsDeviceResult> Handle(ReleaseEmptyGpsDeviceCommand r, CancellationToken ct)
    {
        var fiche = await _context.GpsDevices.IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == r.DeviceId, ct);
        if (fiche == null)
            return new ReleaseEmptyGpsDeviceResult(false, "Fiche boîtier introuvable.", NotFound: true);

        if (fiche.LastCommunication != null)
            return new ReleaseEmptyGpsDeviceResult(false,
                $"La fiche #{fiche.Id} (IMEI {fiche.DeviceUid}) a déjà communiqué (dernière trame le " +
                $"{fiche.LastCommunication:dd/MM/yyyy HH:mm} UTC) : elle ne se libère pas, passez par le remplacement de boîtier.");

        var donnees = await GpsDeviceReplacementPlanner.InspectAsync(_context, fiche.Id, ignoreVehicleId: null, ct);
        if (!donnees.IsStrictlyEmpty)
            return new ReleaseEmptyGpsDeviceResult(false,
                $"La fiche #{fiche.Id} (IMEI {fiche.DeviceUid}) contient des données ({donnees.Describe()}) : elle ne se libère pas.");

        if (r.PourSocieteId.HasValue && fiche.CompanyId != r.PourSocieteId.Value
            && fiche.CompanyId != GpsDeviceReplacementPlanner.IngestDefaultCompanyId)
            return new ReleaseEmptyGpsDeviceResult(false,
                $"La fiche #{fiche.Id} (IMEI {fiche.DeviceUid}) appartient à une autre société : elle ne se libère pas d'ici.");

        // L'audit ne garde que l'id : ce que portait la fiche ne doit pas disparaître avec elle.
        _logger.LogInformation(
            "Fiche boîtier vide libérée : #{DeviceId} IMEI {Imei} MAT {Mat} SIM {Sim} opérateur {Operateur} société {CompanyId} créée le {CreatedAt:u}",
            fiche.Id, fiche.DeviceUid, fiche.Mat, fiche.SimNumber, fiche.SimOperator, fiche.CompanyId, fiche.CreatedAt);

        _context.GpsDevices.Remove(fiche);
        await _context.SaveChangesAsync(ct);
        return new ReleaseEmptyGpsDeviceResult(true,
            $"Fiche boîtier #{fiche.Id} (IMEI {fiche.DeviceUid}, MAT {fiche.Mat ?? "—"}) libérée.");
    }
}
