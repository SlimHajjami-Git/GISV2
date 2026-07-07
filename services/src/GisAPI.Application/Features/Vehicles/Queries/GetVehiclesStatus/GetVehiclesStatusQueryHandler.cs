using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesStatus;

public class GetVehiclesStatusQueryHandler : IRequestHandler<GetVehiclesStatusQuery, List<VehicleStatusDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    // Same "online" window as GetVehiclesWithPositions: a device that hasn't sent
    // a frame in 41 min is considered offline. Keep the two in sync.
    private const double OnlineWindowMinutes = 41;

    public GetVehiclesStatusQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleStatusDto>> Handle(GetVehiclesStatusQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .AsQueryable();

        // Non-admin users only see their assigned vehicles (mirrors GetVehiclesWithPositions).
        if (!isAdmin && userId > 0)
        {
            var assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == userId)
                .Select(uv => uv.VehicleId)
                .ToListAsync(ct);

            if (assignedVehicleIds.Any())
                vehicleQuery = vehicleQuery.Where(v => assignedVehicleIds.Contains(v.Id));
            else
                vehicleQuery = vehicleQuery.Where(v => false);
        }

        // Project ONLY the columns the bell needs. EF turns the GpsDevice access
        // into a LEFT JOIN — no Include, and critically no gps_positions read.
        var rows = await vehicleQuery
            .Select(v => new
            {
                v.Id,
                v.Name,
                v.Plate,
                LastComm = v.GpsDevice != null ? v.GpsDevice.LastCommunication : null
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        return rows
            .Select(v => new VehicleStatusDto(
                v.Id,
                v.Name,
                v.Plate,
                v.LastComm.HasValue && (now - v.LastComm.Value).TotalMinutes < OnlineWindowMinutes,
                v.LastComm))
            .ToList();
    }
}
