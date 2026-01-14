using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

public class GetVehiclesWithPositionsQueryHandler : IRequestHandler<GetVehiclesWithPositionsQuery, List<VehicleWithPositionDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesWithPositionsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleWithPositionDto>> Handle(GetVehiclesWithPositionsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        // Get vehicles with GPS devices
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .ToListAsync(ct);

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Get latest positions using projection to avoid JSONB issues
        var latestPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) && p.CreatedAt == p.RecordedAt)
            .GroupBy(p => p.DeviceId)
            .Select(g => new {
                DeviceId = g.Key,
                Position = g.OrderByDescending(p => p.RecordedAt).Select(p => new {
                    p.Id,
                    p.Latitude,
                    p.Longitude,
                    p.SpeedKph,
                    p.CourseDeg,
                    p.IgnitionOn,
                    p.RecordedAt
                }).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Position, ct);

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 30;

            return new VehicleWithPositionDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Status,
                v.HasGps,
                v.GpsDevice?.DeviceUid,
                lastComm,
                isOnline,
                position != null ? new PositionDto(
                    (int)position.Id, 
                    position.Latitude,
                    position.Longitude,
                    position.SpeedKph ?? 0.0, 
                    position.CourseDeg ?? 0.0,
                    position.IgnitionOn ?? false, 
                    position.RecordedAt
                ) : null
            );
        }).ToList();

        return result;
    }
}
