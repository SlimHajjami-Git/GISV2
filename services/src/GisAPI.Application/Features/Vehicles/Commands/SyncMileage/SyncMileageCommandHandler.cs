using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.SyncMileage;

/// <summary>
/// « Synchroniser » le kilométrage de la fiche sur le dernier compteur reçu du boîtier.
///
/// <para>Incident NEMS L du 09/11/2026 : des boîtiers mis à jour (logiciel R00C32b) envoyaient
/// un compteur 3 à 100 fois trop grand ; cette commande le recopiait SANS contrôle (pour un
/// firmware « L », elle pouvait même faire reculer la fiche) et six fiches ont été écrasées
/// (242… → 589 824 km). Elle applique désormais les règles de l'ingestion (db.rs) : plafond
/// absolu, jamais de recul, saut maximal ; et elle refuse un boîtier dont les données CAN sont
/// déclarées non fiables (gps_devices.fms_untrusted, migration 068).</para>
/// </summary>
public class SyncMileageCommandHandler : IRequestHandler<SyncMileageCommand, SyncMileageResult>
{
    /// <summary>Plafond absolu, comme MAX_VEHICLE_MILEAGE_KM de l'ingestion.</summary>
    public const int MaxMileageKm = 2_000_000;

    /// <summary>Artefact du protocole NEMS (2^20 − 2), jamais un vrai compteur.</summary>
    public const long NemsOdometerArtifact = 1048574;

    /// <summary>Saut toujours admis, comme MAX_ODOMETER_JUMP_KM de l'ingestion.</summary>
    public const int BaseJumpKm = 200;

    /// <summary>
    /// Une fiche synchronisée à la main peut être en retard de plusieurs jours : le saut admis
    /// grandit avec le temps écoulé depuis sa dernière mise à jour, à raison de 1 500 km par
    /// jour (plus qu'un camion ne roule). Le compteur faux de l'incident sautait de 400 000 km.
    /// </summary>
    public const int MaxKmPerDay = 1500;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SyncMileageCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SyncMileageResult> Handle(SyncMileageCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct);

        if (vehicle == null)
            throw new NotFoundException("Vehicle", request.VehicleId);

        var previousMileage = vehicle.Mileage;
        SyncMileageResult Unchanged(string? message) =>
            new(vehicle.Id, previousMileage, vehicle.Mileage, "manual", false, message);

        if (!vehicle.GpsDeviceId.HasValue)
            return Unchanged(null);

        if (vehicle.GpsDevice?.FmsUntrusted == true)
            return Unchanged("Le compteur de ce boîtier est en cours de vérification : synchronisation suspendue. "
                + "Saisissez le kilométrage à la main si nécessaire.");

        var lastOdometer = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value
                     && p.OdometerKm.HasValue
                     && p.OdometerKm > 0
                     && p.OdometerKm != NemsOdometerArtifact)
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => p.OdometerKm)
            .FirstOrDefaultAsync(ct);

        if (!lastOdometer.HasValue || lastOdometer.Value <= 0)
            return Unchanged(null);

        var refusal = Refusal(lastOdometer.Value, vehicle.Mileage, vehicle.UpdatedAt, DateTime.UtcNow);
        if (refusal != null)
            return Unchanged(refusal);

        if (lastOdometer.Value == vehicle.Mileage)
            return Unchanged(null);

        vehicle.Mileage = (int)lastOdometer.Value;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new SyncMileageResult(vehicle.Id, previousMileage, vehicle.Mileage, "gps", true);
    }

    /// <summary>
    /// Raison de refuser ce compteur pour la fiche, ou null s'il est acceptable. Mêmes règles que
    /// l'ingestion : plafond, jamais de recul, saut maximal (voir <see cref="MaxKmPerDay"/>).
    /// </summary>
    public static string? Refusal(long odometerKm, int currentMileage, DateTime? lastUpdatedAt, DateTime now)
    {
        if (odometerKm >= MaxMileageKm)
            return $"Compteur du boîtier invraisemblable ({odometerKm:N0} km) : fiche inchangée.";

        if (odometerKm < currentMileage)
            return $"Le compteur du boîtier ({odometerKm:N0} km) est inférieur au kilométrage de la fiche "
                + $"({currentMileage:N0} km) : un kilométrage ne recule pas, fiche inchangée.";

        // Fiche vide (véhicule neuf dans l'application) : rien à comparer, le plafond suffit.
        if (currentMileage <= 0)
            return null;

        var days = lastUpdatedAt.HasValue ? Math.Max(0, (now - lastUpdatedAt.Value).TotalDays) : 0;
        var maxJump = BaseJumpKm + (long)Math.Ceiling(days * MaxKmPerDay);
        var jump = odometerKm - currentMileage;
        if (jump > maxJump)
            return $"Le compteur du boîtier ({odometerKm:N0} km) dépasse la fiche de {jump:N0} km, "
                + $"plus que le maximum plausible ({maxJump:N0} km) : fiche inchangée.";

        return null;
    }
}
