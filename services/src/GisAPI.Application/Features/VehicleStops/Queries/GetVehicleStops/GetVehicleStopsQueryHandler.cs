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
        var allStops = new List<VehicleStopDto>();
        
        // 1. Get stops from vehicle_stops table
        var stopsFromTable = await GetStopsFromTable(request, ct);
        allStops.AddRange(stopsFromTable);
        
        // 2. Get stops from gps_position where ignition is off
        var stopsFromGps = await GetStopsFromGpsPositions(request, ct);
        
        // 3. Merge and deduplicate - only add GPS stops that don't overlap with table stops
        foreach (var gpsStop in stopsFromGps)
        {
            bool isDuplicate = allStops.Any(existingStop => 
                Math.Abs((existingStop.StartTime - gpsStop.StartTime).TotalMinutes) < 5 &&
                GetDistanceKm(existingStop.Latitude, existingStop.Longitude, gpsStop.Latitude, gpsStop.Longitude) < 0.1);
            
            if (!isDuplicate)
            {
                allStops.Add(gpsStop);
            }
        }
        
        // Sort by start time descending
        allStops = allStops.OrderByDescending(s => s.StartTime).ToList();
        
        var totalCount = allStops.Count;
        
        // Apply pagination
        var paginatedStops = allStops
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new VehicleStopsResultDto(paginatedStops, totalCount, request.Page, request.PageSize);
    }
    
    private async Task<List<VehicleStopDto>> GetStopsFromTable(GetVehicleStopsQuery request, CancellationToken ct)
    {
        var query = _context.VehicleStops
            .AsNoTracking()
            .Include(s => s.Vehicle)
            .Include(s => s.Driver)
            .Include(s => s.Geofence)
            .AsQueryable();

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

        return await query
            .OrderByDescending(s => s.StartTime)
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
    }
    
    private async Task<List<VehicleStopDto>> GetStopsFromGpsPositions(GetVehicleStopsQuery request, CancellationToken ct)
    {
        if (!request.VehicleId.HasValue) return new List<VehicleStopDto>();
        
        // Get vehicle to find device ID
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId.Value, ct);
            
        if (vehicle?.GpsDeviceId == null) return new List<VehicleStopDto>();
        
        // Query GPS positions where ignition is off
        var query = _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value && p.IgnitionOn == false);
            
        if (request.StartDate.HasValue)
        {
            query = query.Where(p => p.RecordedAt >= request.StartDate.Value);
        }

        if (request.EndDate.HasValue)
        {
            query = query.Where(p => p.RecordedAt <= request.EndDate.Value);
        }
        
        var positions = await query
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(ct);
            
        if (!positions.Any()) return new List<VehicleStopDto>();
        
        // Detect stop periods from consecutive ignition-off positions
        var stops = new List<VehicleStopDto>();
        var currentStopStart = positions.First();
        var currentStopEnd = currentStopStart;
        
        for (int i = 1; i < positions.Count; i++)
        {
            var pos = positions[i];
            var timeDiff = (pos.RecordedAt - currentStopEnd.RecordedAt).TotalMinutes;
            var distance = GetDistanceKm(currentStopEnd.Latitude, currentStopEnd.Longitude, pos.Latitude, pos.Longitude);
            
            // If more than 30 min gap or moved more than 500m, it's a new stop
            if (timeDiff > 30 || distance > 0.5)
            {
                // Save current stop if it lasted more than 1 minute
                var duration = (currentStopEnd.RecordedAt - currentStopStart.RecordedAt).TotalSeconds;
                if (duration >= 60)
                {
                    stops.Add(CreateStopDto(vehicle, currentStopStart, currentStopEnd, (int)duration));
                }
                currentStopStart = pos;
            }
            currentStopEnd = pos;
        }
        
        // Don't forget the last stop
        var lastDuration = (currentStopEnd.RecordedAt - currentStopStart.RecordedAt).TotalSeconds;
        if (lastDuration >= 60)
        {
            stops.Add(CreateStopDto(vehicle, currentStopStart, currentStopEnd, (int)lastDuration));
        }
        
        return stops;
    }
    
    private VehicleStopDto CreateStopDto(Domain.Entities.Vehicle vehicle, Domain.Entities.GpsPosition start, Domain.Entities.GpsPosition end, int durationSeconds)
    {
        return new VehicleStopDto(
            Id: 0, // Generated from GPS, no table ID
            VehicleId: vehicle.Id,
            VehicleName: vehicle.Name,
            VehiclePlate: vehicle.Plate,
            DriverId: null,
            DriverName: null,
            StartTime: start.RecordedAt,
            EndTime: end.RecordedAt,
            DurationSeconds: durationSeconds,
            Latitude: start.Latitude,
            Longitude: start.Longitude,
            Address: start.Address,
            StopType: "ignition_off",
            IgnitionOff: true,
            IsAuthorized: true,
            FuelLevelStart: null,
            FuelLevelEnd: null,
            FuelConsumed: null,
            InsideGeofence: false,
            GeofenceName: null,
            Notes: "Détecté depuis GPS (contact coupé)"
        );
    }
    
    private static double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth radius in km
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
    
    private static double ToRad(double deg) => deg * Math.PI / 180;
}
