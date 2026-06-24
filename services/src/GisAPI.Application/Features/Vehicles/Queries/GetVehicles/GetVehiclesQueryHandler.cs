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
            // Batch fetch latest odometer_km for all firmware "L" devices
            var latestOdometers = await _context.GpsPositions
                .Where(p => firmwareLDeviceIds.Contains(p.DeviceId)
                         && p.OdometerKm.HasValue
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .GroupBy(p => p.DeviceId)
                .Select(g => new { DeviceId = g.Key, OdometerKm = g.OrderByDescending(p => p.RecordedAt).First().OdometerKm })
                .ToListAsync(ct);

            var odometerMap = latestOdometers.ToDictionary(x => x.DeviceId, x => x.OdometerKm ?? 0);

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
}



