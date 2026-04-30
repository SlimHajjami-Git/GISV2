using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public class GetVehicleMaintenanceQueryHandler : IRequestHandler<GetVehicleMaintenanceQuery, PaginatedList<VehicleMaintenanceStatusDto>>
{
    private readonly IGisDbContext _context;

    public GetVehicleMaintenanceQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<VehicleMaintenanceStatusDto>> Handle(GetVehicleMaintenanceQuery request, CancellationToken cancellationToken)
    {
        var vehiclesQuery = _context.Vehicles.AsQueryable();

        if (request.VehicleId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.Id == request.VehicleId.Value);

        var vehicles = await vehiclesQuery
            .Include(v => v.GpsDevice)
            .ToListAsync(cancellationToken);

        var schedules = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Where(s => vehicles.Select(v => v.Id).Contains(s.VehicleId))
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.Status))
            schedules = schedules.Where(s => s.Status == request.Status).ToList();

        var today = DateTime.UtcNow.Date;
        var results = new List<VehicleMaintenanceStatusDto>();

        // Firmware "L": batch fetch latest odometer_km for all firmware L devices
        var firmwareLDeviceIds = vehicles
            .Where(v => v.GpsDevice != null
                     && !string.IsNullOrEmpty(v.GpsDevice.FirmwareVersion)
                     && v.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        var odometerMap = new Dictionary<int, long>();
        if (firmwareLDeviceIds.Any())
        {
            var latestOdometers = await _context.GpsPositions
                .Where(p => firmwareLDeviceIds.Contains(p.DeviceId)
                         && p.OdometerKm.HasValue && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .GroupBy(p => p.DeviceId)
                .Select(g => new { DeviceId = g.Key, OdometerKm = g.OrderByDescending(p => p.RecordedAt).First().OdometerKm })
                .ToListAsync(cancellationToken);
            odometerMap = latestOdometers.ToDictionary(x => x.DeviceId, x => x.OdometerKm ?? 0);
        }

        // Calypso 7 (P-maint-couche1, follow-up): batch trips totals so the
        // 3rd fallback in the smart resolver works even when neither GPS
        // odometer nor a positive vehicle.Mileage is available. Without this
        // batch, /entretien-programmable kept showing currentMileage = 0
        // for any vehicle whose tracker has no FMS odometer wired (e.g. the
        // 257 TU 6114 with disconnected CAN bus). Mirrors the cascade in
        // MaintenanceSchedulerService.GetCurrentMileageAsync.
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var tripsTotalsRaw = await _context.Trips
            .Where(t => vehicleIds.Contains(t.VehicleId) && t.EndTime != null)
            .GroupBy(t => t.VehicleId)
            .Select(g => new { VehicleId = g.Key, KmSum = g.Sum(t => (double?)t.DistanceKm) ?? 0 })
            .ToListAsync(cancellationToken);
        var tripsTotalKmMap = tripsTotalsRaw.ToDictionary(x => x.VehicleId, x => (int)Math.Round(x.KmSum));

        foreach (var vehicle in vehicles)
        {
            // Calypso 7 (P-maint-couche1, follow-up #2): MAX-cascade des
            // trois sources pour qu'un trips total qui grimpe écrase un
            // vehicle.Mileage gelé. Sinon les vehicules dont le CAN bus
            // est mort restent figés sur la valeur statique de
            // vehicle.Mileage et l'entretien ne se déclenche jamais.
            long gpsOdo = (vehicle.GpsDevice != null && odometerMap.TryGetValue(vehicle.GpsDevice.Id, out var odo) && odo > 0) ? odo : 0;
            int manualMileage = vehicle.Mileage;
            int tripsMileage = tripsTotalKmMap.TryGetValue(vehicle.Id, out var tripsKm) ? tripsKm : 0;
            int currentMileage = (int)Math.Max(Math.Max(gpsOdo, manualMileage), tripsMileage);

            var vehicleSchedules = schedules.Where(s => s.VehicleId == vehicle.Id).ToList();
            if (vehicleSchedules.Count == 0 && request.Status != null) continue;

            var items = vehicleSchedules.Select(s =>
            {
                var kmUntilDue = s.NextDueKm.HasValue ? s.NextDueKm.Value - currentMileage : (int?)null;
                var daysUntilDue = s.NextDueDate.HasValue ? (int)(s.NextDueDate.Value - today).TotalDays : (int?)null;

                return new MaintenanceItemDto(
                    s.Id,
                    s.TemplateId,
                    s.Template?.Name ?? "",
                    s.Template?.Category ?? "",
                    s.Template?.Priority ?? "medium",
                    s.LastDoneDate,
                    s.LastDoneKm,
                    s.NextDueDate,
                    s.NextDueKm,
                    s.Status,
                    kmUntilDue,
                    daysUntilDue,
                    s.FreeUsesTotal,
                    s.FreeUsesRemaining,
                    s.FreeSource,
                    s.FreeExpiryDate,
                    s.FreeNotes
                );
            }).OrderBy(i => i.Status == "overdue" ? 0 : i.Status == "due" ? 1 : i.Status == "upcoming" ? 2 : 3)
              .ThenBy(i => i.DaysUntilDue ?? int.MaxValue)
              .ToList();

            results.Add(new VehicleMaintenanceStatusDto(
                vehicle.Id,
                vehicle.Name,
                vehicle.Plate,
                currentMileage,
                items
            ));
        }

        var totalCount = results.Count;
        var paginatedItems = results
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PaginatedList<VehicleMaintenanceStatusDto>(paginatedItems, totalCount, request.Page, request.PageSize);
    }
}



