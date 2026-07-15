using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicles;

public class GetVehiclesQueryHandler : IRequestHandler<GetVehiclesQuery, PaginatedList<VehicleDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<VehicleDto>> Handle(GetVehiclesQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        var query = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .AsQueryable();

        // Non-admin users only see their assigned vehicles
        if (!isAdmin && userId > 0)
        {
            var assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == userId)
                .Select(uv => uv.VehicleId)
                .ToListAsync(ct);

            if (assignedVehicleIds.Any())
            {
                query = query.Where(v => assignedVehicleIds.Contains(v.Id));
            }
            else
            {
                // No vehicles assigned = see nothing
                query = query.Where(v => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(v =>
                v.Name.ToLower().Contains(term) ||
                (v.Plate != null && v.Plate.ToLower().Contains(term)) ||
                (v.Brand != null && v.Brand.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(v => v.Status == request.Status);
        }

        var projectedQuery = query
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Year,
                v.Color,
                v.Status,
                v.HasGps,
                v.Mileage,
                v.FuelTankCapacity,
                v.SpeedLimit,
                v.FuelType,
                v.AssignedDriverId,
                v.AssignedDriver != null ? v.AssignedDriver.FullName : null,
                v.AssignedSupervisorId,
                v.AssignedSupervisor != null ? v.AssignedSupervisor.FullName : null,
                v.GpsDevice != null ? new GpsDeviceDto(
                    v.GpsDevice.Id,
                    v.GpsDevice.DeviceUid,
                    v.GpsDevice.Label,
                    v.GpsDevice.Status,
                    v.GpsDevice.LastCommunication,
                    v.GpsDevice.BatteryLevel,
                    v.GpsDevice.SignalStrength,
                    v.GpsDevice.Model,
                    v.GpsDevice.FirmwareVersion
                ) : null,
                v.CreatedAt,
                // Document expiries
                v.InsuranceExpiry,
                v.TechnicalInspectionExpiry,
                v.TaxExpiry,
                v.RegistrationExpiry,
                v.TransportPermitExpiry
            ));

        var result = await projectedQuery.ToPaginatedListAsync(request.Page, request.PageSize, ct);

        // For devices with firmware "L", override mileage from GPS odometer_km
        var firmwareLDeviceIds = result.Items
            .Where(v => v.GpsDevice != null 
                     && !string.IsNullOrEmpty(v.GpsDevice.FirmwareVersion)
                     && v.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        if (firmwareLDeviceIds.Any())
        {
            // Dernier odomètre GPS par boîtier firmware "L".
            //
            // L'ancienne version (GroupBy + OrderByDescending.First avec un
            // FILTRE odometer valide) était traduite par EF en une sous-requête
            // corrélée par boîtier : pour les boîtiers dont les trames récentes
            // portent un odomètre invalide (sentinelle 1048574 / NULL), Postgres
            // remontait des MILLIERS de lignes → 15-36 s pour 219 boîtiers
            // (mesuré EXPLAIN ANALYZE sur prod). C'était LA lenteur de la page
            // véhicules (et du picker de véhicules dans la gestion utilisateurs).
            //
            // On récupère désormais la trame LA PLUS RÉCENTE par boîtier via un
            // LATERAL JOIN — une seule lecture d'index par boîtier, aucun filtre
            // odomètre côté SQL (donc aucune remontée) : 6 ms pour 219 boîtiers.
            // La validité de l'odomètre (>0, ≠ sentinelle) est contrôlée en C#
            // sur cette seule trame ; si elle est invalide, on garde le
            // kilométrage stocké du véhicule (repli acceptable et rare).
            const string odoSql = @"
SELECT d.device_id AS ""DeviceId"", lp.odometer_km AS ""OdometerKm""
FROM unnest({0}::integer[]) AS d(device_id)
CROSS JOIN LATERAL (
    SELECT odometer_km
    FROM gps_positions
    WHERE device_id = d.device_id
    ORDER BY recorded_at DESC
    LIMIT 1
) lp;
";
            var latestOdometers = await _context.Database
                .SqlQueryRaw<DeviceOdometerRow>(odoSql, firmwareLDeviceIds.ToArray())
                .ToListAsync(ct);

            var odometerMap = latestOdometers
                .Where(x => x.OdometerKm is > 0 and not 1048574)
                .ToDictionary(x => x.DeviceId, x => (long)x.OdometerKm!.Value);

            result = new PaginatedList<VehicleDto>(
                result.Items.Select(v =>
                {
                    if (v.GpsDevice != null && odometerMap.TryGetValue(v.GpsDevice.Id, out var odo) && odo > 0)
                        return v with { Mileage = (int)odo };
                    return v;
                }).ToList(),
                result.TotalCount,
                result.PageNumber,
                request.PageSize
            );
        }

        return result;
    }

    /// <summary>Dernier odomètre GPS d'un boîtier (projection SqlQueryRaw).</summary>
    public class DeviceOdometerRow
    {
        public int DeviceId { get; set; }
        public long? OdometerKm { get; set; }
    }
}



