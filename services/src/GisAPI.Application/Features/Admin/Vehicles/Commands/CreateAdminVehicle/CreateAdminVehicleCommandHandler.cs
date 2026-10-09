using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;

public class CreateAdminVehicleCommandHandler : IRequestHandler<CreateAdminVehicleCommand, CreateAdminVehicleResult>
{
    private readonly IGisDbContext _context;

    public CreateAdminVehicleCommandHandler(IGisDbContext context) => _context = context;

    public async Task<CreateAdminVehicleResult> Handle(CreateAdminVehicleCommand r, CancellationToken ct)
    {
        var company = await _context.Societes.FindAsync(new object[] { r.CompanyId }, ct);
        if (company == null)
            return new CreateAdminVehicleResult(false, "Société non trouvée");

        // Même règle que l'espace client (DEF-037) : l'administration créait un
        // second « GA-214-RK » dans la société.
        var plateClash = await VehicleWriteRules.FindPlateClashAsync(_context, r.CompanyId, r.Plate, null, null, ct);
        if (plateClash != null)
            return new CreateAdminVehicleResult(false, VehicleWriteRules.AdminPlateClashMessage(plateClash));

        var vehicle = new Vehicle
        {
            Name = r.Name,
            Type = r.Type ?? "camion",
            Brand = r.Brand,
            Model = r.Model,
            Plate = r.Plate,
            Year = r.Year,
            Color = r.Color,
            Status = r.Status ?? "available",
            HasGps = r.HasGps,
            Mileage = r.Mileage ?? 0,
            FuelType = r.FuelType ?? "diesel",
            FuelTankCapacity = r.FuelTankCapacity,
            CompanyId = r.CompanyId
        };

        if (r.HasGps)
        {
            var (gpsDevice, error) = await GpsDeviceResolver.ResolveAsync(
                _context, r.CompanyId, r.GpsDeviceId, r.GpsImei, r.GpsMat);

            if (error != null) return new CreateAdminVehicleResult(false, error);
            if (gpsDevice == null) return new CreateAdminVehicleResult(false, "Impossible d'associer le GPS sans IMEI ou appareil existant.");

            vehicle.GpsDeviceId = gpsDevice.Id;
            vehicle.HasGps = true;
            gpsDevice.Status = "assigned";
            gpsDevice.Vehicle = vehicle;

            // Même règle que GpsDeviceResolver : une fiche existante qui a déjà communiqué
            // garde l'orthographe de son MAT (l'ingestion le compare à l'identique) si la
            // saisie n'en diffère que par la casse ou des espaces. Sans cela, cette ligne
            // réécrivait la saisie brute juste après le Resolver.
            if (!string.IsNullOrWhiteSpace(r.GpsMat))
                gpsDevice.Mat = GpsDeviceUniquenessGuard.StoredValueFor(
                    gpsDevice.Mat, r.GpsMat, keepDeviceSpelling: gpsDevice.LastCommunication != null);
            if (!string.IsNullOrWhiteSpace(r.GpsBrand)) gpsDevice.Brand = r.GpsBrand;
            if (!string.IsNullOrWhiteSpace(r.GpsModel)) gpsDevice.Model = r.GpsModel;
            if (!string.IsNullOrWhiteSpace(r.GpsFirmwareVersion)) gpsDevice.FirmwareVersion = r.GpsFirmwareVersion;
            if (!string.IsNullOrWhiteSpace(r.GpsFuelSensorMode)) gpsDevice.FuelSensorMode = r.GpsFuelSensorMode;
            if (!string.IsNullOrWhiteSpace(r.GpsSimNumber)) gpsDevice.SimNumber = r.GpsSimNumber.Trim();
            if (!string.IsNullOrWhiteSpace(r.GpsSimOperator)) gpsDevice.SimOperator = r.GpsSimOperator;
            if (r.GpsInstallationDate.HasValue) gpsDevice.InstallationDate = r.GpsInstallationDate;

            // Anti-doublons IMEI/MAT/SIM sur les valeurs FINALES du boîtier
            // (gpsDevice.Id = 0 pour un boîtier neuf → comparé à toute la base).
            var conflict = await GpsDeviceUniquenessGuard.FindConflictDetailAsync(
                _context, gpsDevice.Id, gpsDevice.DeviceUid, gpsDevice.Mat, gpsDevice.SimNumber, ct);
            if (conflict != null) return await DecrireConflitAsync(conflict, r.CompanyId, ct);
        }

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync(ct);

        // Reload with navigations
        var created = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstAsync(v => v.Id == vehicle.Id, ct);

        return new CreateAdminVehicleResult(true, Vehicle: GpsDeviceResolver.MapToDto(created));
    }

    /// <summary>
    /// Message de doublon à la création — et fiche morte à libérer, s'il y en a une.
    ///
    /// POURQUOI — recette du 09/10/2026 (Belive SAV, HTZ 344) : le véhicule avait été supprimé,
    /// mais sa fiche boîtier (IMEI à 14 chiffres, jamais connectée) survivait « non affectée »
    /// avec son MAT et sa SIM ; la re-création butait dessus et aucun écran ne permettait de la
    /// libérer. Quand la fiche en conflit n'est rattachée à rien, n'a jamais communiqué et ne
    /// contient aucune donnée, on le dit et on propose sa libération — la règle du remplacement
    /// de boîtier, qui ne supprime jamais qu'une fiche strictement vide.
    /// </summary>
    private async Task<CreateAdminVehicleResult> DecrireConflitAsync(GpsDeviceConflict conflict, int companyId, CancellationToken ct)
    {
        // Un doublon d'IMEI n'est jamais une fiche à libérer : c'est LE boîtier à réutiliser (le
        // résolveur le retrouve par l'IMEI exact ; s'il ne l'a pas trouvé, l'IMEI est mal écrit).
        if (conflict.VehicleId != null || conflict.Identifier == GpsDeviceConflict.Imei)
            return new CreateAdminVehicleResult(false, conflict.Message);

        var fiche = await _context.GpsDevices.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Id == conflict.DeviceId, ct);
        if (fiche == null || fiche.LastCommunication != null)
            return new CreateAdminVehicleResult(false, conflict.Message);

        var donnees = await GpsDeviceReplacementPlanner.InspectAsync(_context, fiche.Id, ignoreVehicleId: null, ct);
        if (!donnees.IsStrictlyEmpty) return new CreateAdminVehicleResult(false, conflict.Message);

        // Une fiche vide d'un AUTRE client (pré-enregistrée à la réception du matériel) ne se
        // libère pas depuis la création d'un véhicule chez un autre : on la nomme, c'est tout.
        // La société par défaut de l'ingestion n'est pas un client.
        if (fiche.CompanyId != companyId && fiche.CompanyId != GpsDeviceReplacementPlanner.IngestDefaultCompanyId)
        {
            var societe = await _context.Societes.AsNoTracking().IgnoreQueryFilters()
                .Where(s => s.Id == fiche.CompanyId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            return new CreateAdminVehicleResult(false,
                $"{conflict.Message} Ce boîtier #{fiche.Id} appartient à la société « {societe ?? fiche.CompanyId.ToString()} » " +
                "(jamais connecté, sans véhicule ni donnée) : vérifiez avec elle avant de le réutiliser ou de le libérer.");
        }

        var imei = GpsDeviceUniquenessGuard.IsValidImei(fiche.DeviceUid)
            ? $"IMEI {fiche.DeviceUid}"
            : $"IMEI {fiche.DeviceUid}, qui n'a pas la forme d'un IMEI valide";
        var message = $"{conflict.Message} Ce boîtier #{fiche.Id} ({imei}) n'a jamais communiqué, n'est rattaché " +
                      "à aucun véhicule et ne contient aucune donnée : c'est très probablement une ancienne fiche " +
                      "saisie à la main. Vous pouvez la libérer, puis recommencer.";
        return new CreateAdminVehicleResult(false, message, ReleaseSuggestedDeviceId: fiche.Id);
    }
}
