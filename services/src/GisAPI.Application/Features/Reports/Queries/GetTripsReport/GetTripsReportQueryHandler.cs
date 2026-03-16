using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Queries.GetTripsReport;

public class GetTripsReportQueryHandler : IRequestHandler<GetTripsReportQuery, TripsReportDto>
{
    private readonly IGisDbContext _context;

    public GetTripsReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<TripsReportDto> Handle(GetTripsReportQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);

        if (vehicle == null)
        {
            return new TripsReportDto
            {
                VehicleId = request.VehicleId,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
        }

        if (!vehicle.GpsDeviceId.HasValue)
        {
            return new TripsReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
        }

        var rangeStart = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
        var rangeEnd = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1), DateTimeKind.Utc);

        // Projection: only load columns needed for trip detection
        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= rangeStart &&
                        p.RecordedAt < rangeEnd)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new TripPositionSlim
            {
                RecordedAt = p.RecordedAt,
                IgnitionOn = p.IgnitionOn,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph
            })
            .ToListAsync(ct);

        var allTrips = DetectTrips(positions);
        // No geocoding — frontend handles address enrichment via enrichAllAddresses()

        // Group trips by day
        var days = allTrips
            .GroupBy(t => t.StartTime.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayTrips = g.OrderBy(t => t.StartTime).ToList();
                var totalSeconds = dayTrips.Sum(t => t.DurationSeconds);
                var totalDist = dayTrips.Sum(t => t.DistanceKm);
                return new TripDayDto
                {
                    Date = g.Key,
                    Trips = dayTrips,
                    TotalTripSeconds = totalSeconds,
                    TotalTripFormatted = FormatDuration(totalSeconds),
                    TotalDistanceKm = Math.Round(totalDist, 2),
                    TripCount = dayTrips.Count
                };
            })
            .ToList();

        // Build summary
        var totalTripSeconds = allTrips.Sum(t => t.DurationSeconds);
        var totalDistanceKm = allTrips.Sum(t => t.DistanceKm);
        var maxSpeed = allTrips.Count > 0 ? allTrips.Max(t => t.MaxSpeedKph) : 0;
        var avgSpeed = allTrips.Count > 0 ? allTrips.Average(t => t.AvgSpeedKph) : 0;
        var avgDist = allTrips.Count > 0 ? totalDistanceKm / allTrips.Count : 0;

        return new TripsReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Days = days,
            Summary = new TripsSummaryDto
            {
                TotalTrips = allTrips.Count,
                TotalTripSeconds = totalTripSeconds,
                TotalTripFormatted = FormatDuration(totalTripSeconds),
                TotalDistanceKm = Math.Round(totalDistanceKm, 2),
                MaxSpeedKph = Math.Round(maxSpeed, 1),
                AvgSpeedKph = Math.Round(avgSpeed, 1),
                AvgDistanceKm = Math.Round(avgDist, 2)
            }
        };
    }

    /// <summary>
    /// Detect trips from GPS positions based on ignition_on == true.
    /// A trip = continuous period where ignition is ON.
    /// Adjacent trips separated by < 60s gap are merged (noisy ignition debounce).
    /// Distance calculated via Haversine with GPS noise filter.
    /// </summary>
    internal static List<TripEntryDto> DetectTrips(List<TripPositionSlim> positions)
    {
        if (positions.Count == 0) return new List<TripEntryDto>();

        // Step 1: Find raw continuous periods of ignition ON
        var rawTrips = new List<(DateTime Start, DateTime End, List<TripPositionSlim> Positions)>();
        List<TripPositionSlim>? currentPositions = null;

        foreach (var pos in positions)
        {
            var isOn = pos.IgnitionOn == true;

            if (isOn)
            {
                if (currentPositions == null)
                {
                    currentPositions = new List<TripPositionSlim> { pos };
                }
                else
                {
                    currentPositions.Add(pos);
                }
            }
            else
            {
                if (currentPositions != null && currentPositions.Count > 0)
                {
                    rawTrips.Add((
                        currentPositions[0].RecordedAt,
                        currentPositions[^1].RecordedAt,
                        currentPositions
                    ));
                    currentPositions = null;
                }
            }
        }
        // Trailing trip (ignition still ON at end of data)
        if (currentPositions != null && currentPositions.Count > 0)
        {
            rawTrips.Add((
                currentPositions[0].RecordedAt,
                currentPositions[^1].RecordedAt,
                currentPositions
            ));
        }

        // Step 2: Merge adjacent trips separated by < 60s gap (noisy ignition debounce)
        const int MERGE_GAP_SECONDS = 60;
        var merged = new List<(DateTime Start, DateTime End, List<TripPositionSlim> Positions)>();

        foreach (var trip in rawTrips)
        {
            if (merged.Count > 0)
            {
                var prev = merged[^1];
                if ((trip.Start - prev.End).TotalSeconds < MERGE_GAP_SECONDS)
                {
                    // Merge: combine positions
                    prev.Positions.AddRange(trip.Positions);
                    merged[^1] = (prev.Start, trip.End, prev.Positions);
                    continue;
                }
            }
            merged.Add((trip.Start, trip.End, new List<TripPositionSlim>(trip.Positions)));
        }

        // Step 3: Build trip DTOs with distance calculation
        var result = new List<TripEntryDto>();
        foreach (var trip in merged)
        {
            var durationSeconds = (int)(trip.End - trip.Start).TotalSeconds;
            if (durationSeconds < 60) continue; // Skip micro-trips < 1 min

            var distanceKm = CalculateDistance(trip.Positions);
            if (distanceKm < 0.1) continue; // Skip trips with no real movement

            var speedPositions = trip.Positions.Where(p => (p.SpeedKph ?? 0) > 0).ToList();
            var maxSpeed = trip.Positions.Count > 0 ? trip.Positions.Max(p => p.SpeedKph ?? 0) : 0;
            var avgSpeed = speedPositions.Count > 0 ? speedPositions.Average(p => p.SpeedKph ?? 0) : 0;

            var startPos = trip.Positions[0];
            var endPos = trip.Positions[^1];

            result.Add(new TripEntryDto
            {
                StartTime = trip.Start,
                EndTime = trip.End,
                DurationSeconds = durationSeconds,
                DurationFormatted = FormatDuration(durationSeconds),
                DistanceKm = Math.Round(distanceKm, 2),
                MaxSpeedKph = Math.Round(maxSpeed, 1),
                AvgSpeedKph = Math.Round(avgSpeed, 1),
                StartLatitude = startPos.Latitude,
                StartLongitude = startPos.Longitude,
                EndLatitude = endPos.Latitude,
                EndLongitude = endPos.Longitude
            });
        }

        return result;
    }

    private static double CalculateDistance(List<TripPositionSlim> positions)
    {
        double totalDistance = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var dist = HaversineDistance(
                positions[i - 1].Latitude, positions[i - 1].Longitude,
                positions[i].Latitude, positions[i].Longitude);
            // Filter GPS noise: min 10m, max 5km jump, speed > 2 km/h
            if (dist > 0.01 && dist < 5 && (positions[i].SpeedKph ?? 0) > 2)
                totalDistance += dist;
        }
        return totalDistance;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    internal static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}min" : $"{hours}h";
        return $"{minutes}min";
    }
}

public class GetTripsReportAllVehiclesQueryHandler : IRequestHandler<GetTripsReportAllVehiclesQuery, List<TripsReportDto>>
{
    private readonly IGisDbContext _context;
    private readonly IMediator _mediator;

    public GetTripsReportAllVehiclesQueryHandler(IGisDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<List<TripsReportDto>> Handle(GetTripsReportAllVehiclesQuery request, CancellationToken ct)
    {
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.GpsDeviceId.HasValue);

        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicleIds = await vehiclesQuery.Select(v => v.Id).ToListAsync(ct);

        // Sequential: DbContext is not thread-safe, process one vehicle at a time
        var results = new List<TripsReportDto>(vehicleIds.Count);
        foreach (var vehicleId in vehicleIds)
        {
            var report = await _mediator.Send(new GetTripsReportQuery(
                vehicleId, request.StartDate, request.EndDate), ct);
            results.Add(report);
        }
        return results;
    }
}
