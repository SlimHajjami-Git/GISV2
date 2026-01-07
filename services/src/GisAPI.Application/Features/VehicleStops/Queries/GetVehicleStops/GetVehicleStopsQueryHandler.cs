using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleStops.Queries.GetVehicleStops;

public class GetVehicleStopsQueryHandler : IRequestHandler<GetVehicleStopsQuery, VehicleStopsResultDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleStopsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VehicleStopsResultDto> Handle(GetVehicleStopsQuery request, CancellationToken ct)
    {
        var query = _context.VehicleStops
            .AsNoTracking()
            .Include(s => s.Vehicle)
            .Include(s => s.Driver)
            .Include(s => s.Geofence)
            .AsQueryable();

        // Apply filters
        if (request.VehicleId.HasValue)
        {
            query = query.Where(s => s.VehicleId == request.VehicleId.Value);
        }

        if (request.StartDate.HasValue)
        {
            query = query.Where(s => s.StartTime >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(s => s.StartTime <= request.EndDate.Value);
        }

        if (!string.IsNullOrEmpty(request.StopType))
        {
            query = query.Where(s => s.StopType == request.StopType);
        }

        // Get total count
        var totalCount = await query.CountAsync(ct);

        // Apply pagination and ordering
        var stops = await query
            .OrderByDescending(s => s.StartTime)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new VehicleStopDto(
                s.Id,
                s.VehicleId,
                s.Vehicle != null ? s.Vehicle.Name : null,
                s.Vehicle != null ? s.Vehicle.Plate : null,
                s.DriverId,
                s.Driver != null ? s.Driver.Name : null,
                s.StartTime,
                s.EndTime,
                s.DurationSeconds,
                s.Latitude,
                s.Longitude,
                s.Address,
                s.StopType,
                s.IgnitionOff,
                s.IsAuthorized,
                s.FuelLevelStart,
                s.FuelLevelEnd,
                s.FuelLevelStart.HasValue && s.FuelLevelEnd.HasValue 
                    ? s.FuelLevelStart.Value - s.FuelLevelEnd.Value 
                    : null,
                s.InsideGeofence,
                s.Geofence != null ? s.Geofence.Name : null,
                s.Notes
            ))
            .ToListAsync(ct);

        return new VehicleStopsResultDto(stops, totalCount, request.Page, request.PageSize);
    }
}
