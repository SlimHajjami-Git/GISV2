using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Queries;

/// <summary>
/// Data for the /fleet "Limites de vitesse" page. For each vehicle returns
/// its stored km/h limit plus whether that limit was actually delivered to
/// the boitier (latest SPEED_LIMIT DeviceCommand in status 'sent').
///
/// The UI shows an empty field when <c>Sent == false</c> — i.e. the limit
/// exists in the DB but the AJ+CONFN command never reached the device
/// (offline, never synced after a backfill, no/Non-NEMS device, etc.),
/// giving the operator a clear "not programmed yet" signal.
/// </summary>
public record GetVehicleSpeedLimitsQuery() : IRequest<List<VehicleSpeedLimitDto>>;

public record VehicleSpeedLimitDto(
    int VehicleId,
    string Name,
    string? Plate,
    int? SpeedLimitKmh,
    bool Sent
);

public class GetVehicleSpeedLimitsQueryHandler
    : IRequestHandler<GetVehicleSpeedLimitsQuery, List<VehicleSpeedLimitDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleSpeedLimitsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleSpeedLimitDto>> Handle(GetVehicleSpeedLimitsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .OrderBy(v => v.Name)
            .Select(v => new { v.Id, v.Name, v.Plate, v.SpeedLimit })
            .ToListAsync(ct);

        if (vehicles.Count == 0) return new List<VehicleSpeedLimitDto>();

        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        // Latest SPEED_LIMIT command per vehicle. Small dataset (per company
        // fleet), so we pull the rows and reduce in memory rather than fight
        // the LINQ provider for a per-group "latest".
        var latestSentByVehicle = (await _context.DeviceCommands
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId
                         && c.CommandType == "SPEED_LIMIT"
                         && c.VehicleId != null
                         && vehicleIds.Contains(c.VehicleId.Value))
                .Select(c => new { c.VehicleId, c.Status, c.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(c => c.VehicleId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => c.CreatedAt).First().Status == "sent");

        return vehicles.Select(v => new VehicleSpeedLimitDto(
            v.Id,
            v.Name,
            v.Plate,
            v.SpeedLimit,
            latestSentByVehicle.TryGetValue(v.Id, out var sent) && sent
        )).ToList();
    }
}
