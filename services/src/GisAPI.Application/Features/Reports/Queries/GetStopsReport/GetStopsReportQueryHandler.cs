using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Queries.GetStopsReport;

public class GetStopsReportQueryHandler : IRequestHandler<GetStopsReportQuery, StopsReportDto>
{
    private readonly IGisDbContext _context;

    public GetStopsReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<StopsReportDto> Handle(GetStopsReportQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);

        if (vehicle == null)
        {
            return new StopsReportDto
            {
                VehicleId = request.VehicleId,
                StartDate = request.StartDate,
                EndDate = request.EndDate
            };
        }

        if (!vehicle.GpsDeviceId.HasValue)
        {
            return new StopsReportDto
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

        // Projection: only load columns needed for stop detection (much faster than full entity)
        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= rangeStart &&
                        p.RecordedAt < rangeEnd)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new PositionSlim
            {
                RecordedAt = p.RecordedAt,
                IgnitionOn = p.IgnitionOn,
                Latitude = p.Latitude,
                Longitude = p.Longitude
            })
            .ToListAsync(ct);

        var allStops = DetectStops(positions, request.MinStopDurationSeconds);
        // No geocoding here — frontend handles address enrichment via enrichAllAddresses()

        // Fix boundary stops: if vehicle was already stopped at range start, extend to midnight
        if (allStops.Count > 0 && positions.Count > 0)
        {
            var firstStop = allStops[0];
            var firstPos = positions[0];
            // If the first position is ignition OFF and the first stop starts at that position,
            // the vehicle was already stopped before our query range → extend to range start
            if (firstPos.IgnitionOn == false && firstStop.StartTime == firstPos.RecordedAt)
            {
                firstStop.StartTime = rangeStart;
                firstStop.DurationSeconds = (int)(firstStop.EndTime - firstStop.StartTime).TotalSeconds;
                firstStop.DurationFormatted = FormatDuration(firstStop.DurationSeconds);
            }

            var lastStop = allStops[^1];
            var lastPos = positions[^1];
            // If the last position is ignition OFF and the last stop ends at that position,
            // the vehicle is still stopped → extend to range end (or now if today)
            if (lastPos.IgnitionOn == false && lastStop.EndTime == lastPos.RecordedAt)
            {
                var now = DateTime.UtcNow;
                lastStop.EndTime = now < rangeEnd ? now : rangeEnd;
                lastStop.DurationSeconds = (int)(lastStop.EndTime - lastStop.StartTime).TotalSeconds;
                lastStop.DurationFormatted = FormatDuration(lastStop.DurationSeconds);
            }
        }

        // Group stops by day
        var days = allStops
            .GroupBy(s => s.StartTime.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var dayStops = g.OrderBy(s => s.StartTime).ToList();
                var totalSeconds = dayStops.Sum(s => s.DurationSeconds);
                return new StopDayDto
                {
                    Date = g.Key,
                    Stops = dayStops,
                    TotalStopSeconds = totalSeconds,
                    TotalStopFormatted = FormatDuration(totalSeconds),
                    StopCount = dayStops.Count
                };
            })
            .ToList();

        // Build summary
        var totalStopSeconds = allStops.Sum(s => s.DurationSeconds);
        var avgStopSeconds = allStops.Count > 0 ? totalStopSeconds / allStops.Count : 0;
        var maxStopSeconds = allStops.Count > 0 ? allStops.Max(s => s.DurationSeconds) : 0;
        var minStopSeconds = allStops.Count > 0 ? allStops.Min(s => s.DurationSeconds) : 0;

        return new StopsReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Days = days,
            Summary = new StopsSummaryDto
            {
                TotalStops = allStops.Count,
                TotalStopSeconds = totalStopSeconds,
                TotalStopFormatted = FormatDuration(totalStopSeconds),
                AvgStopSeconds = avgStopSeconds,
                AvgStopFormatted = FormatDuration(avgStopSeconds),
                MaxStopSeconds = maxStopSeconds,
                MaxStopFormatted = FormatDuration(maxStopSeconds),
                MinStopSeconds = minStopSeconds,
                MinStopFormatted = FormatDuration(minStopSeconds)
            }
        };
    }

    /// <summary>
    /// Detect stops from GPS positions based on ignition_on == false.
    /// A stop = continuous period where ignition is OFF.
    /// Stop end is extended to the next ignition-ON position (covers GPS data gaps).
    /// Adjacent stops separated by < 60s gap are merged (noisy ignition debounce).
    /// </summary>
    internal static List<StopEntryDto> DetectStops(List<PositionSlim> positions, int minDurationSeconds)
    {
        if (positions.Count == 0) return new List<StopEntryDto>();

        // Step 1: Find raw continuous periods of ignition OFF.
        // When a stop ends, extend its end time to the NEXT position (ignition ON),
        // because the vehicle was still stopped during any GPS data gap.
        var rawStops = new List<(DateTime Start, DateTime End, double Lat, double Lon)>();
        DateTime? stopStart = null;
        DateTime stopEnd = default;
        double stopLat = 0, stopLon = 0;

        for (int i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var isOff = pos.IgnitionOn == false;

            if (isOff)
            {
                if (stopStart == null)
                {
                    stopStart = pos.RecordedAt;
                    stopLat = pos.Latitude;
                    stopLon = pos.Longitude;
                }
                stopEnd = pos.RecordedAt;
            }
            else
            {
                if (stopStart != null)
                {
                    // Extend stop end to this position's time (first ignition-ON after stop).
                    // The vehicle was still stopped until it turned on.
                    rawStops.Add((stopStart.Value, pos.RecordedAt, stopLat, stopLon));
                    stopStart = null;
                }
            }
        }
        // Trailing stop (ignition still OFF at end of data)
        if (stopStart != null)
        {
            rawStops.Add((stopStart.Value, stopEnd, stopLat, stopLon));
        }

        // Step 2: Merge adjacent stops separated by < 60s gap (noisy ignition debounce)
        const int MERGE_GAP_SECONDS = 60;
        var merged = new List<(DateTime Start, DateTime End, double Lat, double Lon)>();

        foreach (var stop in rawStops)
        {
            if (merged.Count > 0)
            {
                var prev = merged[^1];
                if ((stop.Start - prev.End).TotalSeconds < MERGE_GAP_SECONDS)
                {
                    // Merge: extend previous stop
                    merged[^1] = (prev.Start, stop.End > prev.End ? stop.End : prev.End, prev.Lat, prev.Lon);
                    continue;
                }
            }
            merged.Add(stop);
        }

        // Step 3: Filter by minimum duration and build DTOs
        var result = new List<StopEntryDto>();
        foreach (var stop in merged)
        {
            var durationSeconds = (int)(stop.End - stop.Start).TotalSeconds;
            if (durationSeconds >= minDurationSeconds)
            {
                result.Add(new StopEntryDto
                {
                    StartTime = stop.Start,
                    EndTime = stop.End,
                    DurationSeconds = durationSeconds,
                    DurationFormatted = FormatDuration(durationSeconds),
                    Latitude = stop.Lat,
                    Longitude = stop.Lon
                });
            }
        }

        return result;
    }

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

public class GetStopsReportAllVehiclesQueryHandler : IRequestHandler<GetStopsReportAllVehiclesQuery, List<StopsReportDto>>
{
    private readonly IGisDbContext _context;
    private readonly IMediator _mediator;

    public GetStopsReportAllVehiclesQueryHandler(IGisDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<List<StopsReportDto>> Handle(GetStopsReportAllVehiclesQuery request, CancellationToken ct)
    {
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.GpsDeviceId.HasValue);

        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicleIds = await vehiclesQuery.Select(v => v.Id).ToListAsync(ct);
        var reports = new List<StopsReportDto>();

        // Parallel: process all vehicles concurrently
        var tasks = vehicleIds.Select(vehicleId =>
            _mediator.Send(new GetStopsReportQuery(
                vehicleId,
                request.StartDate,
                request.EndDate,
                request.MinStopDurationSeconds), ct));

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
