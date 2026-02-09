using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;

public class GetDailyActivityReportQueryHandler : IRequestHandler<GetDailyActivityReportQuery, DailyActivityReportDto>
{
    private readonly IGisDbContext _context;
    private readonly IGeocodingService _geocodingService;

    public GetDailyActivityReportQueryHandler(IGisDbContext context, IGeocodingService geocodingService)
    {
        _context = context;
        _geocodingService = geocodingService;
    }

    public async Task<DailyActivityReportDto> Handle(GetDailyActivityReportQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);

        if (vehicle == null)
        {
            return new DailyActivityReportDto
            {
                VehicleId = request.VehicleId,
                ReportDate = request.Date,
                HasActivity = false
            };
        }

        if (!vehicle.GpsDeviceId.HasValue)
        {
            return new DailyActivityReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                ReportDate = request.Date,
                HasActivity = false
            };
        }

        // DB stores local time directly (no timezone offset)
        var dayStart = DateTime.SpecifyKind(request.Date.Date, DateTimeKind.Utc);
        var dayEnd = DateTime.SpecifyKind(request.Date.Date.AddDays(1), DateTimeKind.Utc);

        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= dayStart &&
                        p.RecordedAt < dayEnd)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(ct);

        if (!positions.Any())
        {
            return new DailyActivityReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                ReportDate = request.Date,
                HasActivity = false
            };
        }

        return await ProcessDailyActivityAsync(
            vehicle, request.Date, positions,
            request.MinStopDurationSeconds, request.StopSpeedThresholdKph, ct);
    }

    private async Task<DailyActivityReportDto> ProcessDailyActivityAsync(
        Vehicle vehicle,
        DateTime reportDate,
        List<GpsPosition> positions,
        int minStopDurationSeconds,
        double stopSpeedThresholdKph,
        CancellationToken ct)
    {
        var activities = new List<ActivitySegmentDto>();
        var report = new DailyActivityReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            DriverName = vehicle.AssignedDriver?.FullName,
            ReportDate = reportDate,
            HasActivity = true
        };

        // Find first ignition on
        var firstIgnitionOn = positions.FirstOrDefault(p => p.IgnitionOn == true);
        if (firstIgnitionOn != null)
        {
            var address = await _geocodingService.ReverseGeocodeAsync(
                firstIgnitionOn.Latitude, firstIgnitionOn.Longitude);

            report.FirstStart = new DailyStartEventDto
            {
                Timestamp = firstIgnitionOn.RecordedAt,
                Latitude = firstIgnitionOn.Latitude,
                Longitude = firstIgnitionOn.Longitude,
                Address = address ?? firstIgnitionOn.Address
            };
        }

        // Set last position
        var lastPos = positions.Last();
        report.LastPosition = new DailyEndEventDto
        {
            Timestamp = lastPos.RecordedAt,
            Latitude = lastPos.Latitude,
            Longitude = lastPos.Longitude,
            Address = lastPos.Address,
            IgnitionOn = lastPos.IgnitionOn ?? false
        };

        // Process activity segments
        int sequenceNumber = 0;
        int i = 0;

        while (i < positions.Count)
        {
            var currentPos = positions[i];
            var isMoving = (currentPos.SpeedKph ?? 0) > stopSpeedThresholdKph;
            var ignitionOn = currentPos.IgnitionOn ?? false;

            if (isMoving || ignitionOn)
            {
                // Start of a drive segment
                var driveStart = currentPos;
                var drivePositions = new List<GpsPosition> { currentPos };
                i++;

                while (i < positions.Count)
                {
                    var nextPos = positions[i];
                    var nextSpeed = nextPos.SpeedKph ?? 0;
                    var nextIgnition = nextPos.IgnitionOn ?? false;

                    if (nextSpeed > stopSpeedThresholdKph ||
                        (nextIgnition && nextSpeed > 0))
                    {
                        drivePositions.Add(nextPos);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (drivePositions.Count > 1)
                {
                    sequenceNumber++;
                    var driveEnd = drivePositions.Last();
                    var driveDuration = (int)(driveEnd.RecordedAt - driveStart.RecordedAt).TotalSeconds;
                    var speedPositions = drivePositions.Where(p => p.SpeedKph > 0).ToList();
                    var avgSpeed = speedPositions.Any() ? speedPositions.Average(p => p.SpeedKph ?? 0) : 0;
                    var maxSpeed = drivePositions.Max(p => p.SpeedKph ?? 0);
                    var distance = CalculateDistance(drivePositions);

                    activities.Add(new ActivitySegmentDto
                    {
                        Type = "drive",
                        SequenceNumber = sequenceNumber,
                        StartTime = driveStart.RecordedAt,
                        EndTime = driveEnd.RecordedAt,
                        DurationSeconds = driveDuration,
                        DurationFormatted = FormatDuration(driveDuration),
                        StartLocation = new LocationDto
                        {
                            Latitude = driveStart.Latitude,
                            Longitude = driveStart.Longitude,
                            Address = driveStart.Address
                        },
                        EndLocation = new LocationDto
                        {
                            Latitude = driveEnd.Latitude,
                            Longitude = driveEnd.Longitude,
                            Address = driveEnd.Address
                        },
                        DistanceKm = Math.Round(distance, 2),
                        AvgSpeedKph = Math.Round(avgSpeed, 1),
                        MaxSpeedKph = Math.Round(maxSpeed, 1)
                    });
                }
            }
            else
            {
                // Start of a stop segment
                var stopStart = currentPos;
                var stopPositions = new List<GpsPosition> { currentPos };
                i++;

                while (i < positions.Count)
                {
                    var nextPos = positions[i];
                    var nextSpeed = nextPos.SpeedKph ?? 0;

                    if (nextSpeed <= stopSpeedThresholdKph)
                    {
                        stopPositions.Add(nextPos);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                var stopEnd = stopPositions.Last();
                var stopDuration = (int)(stopEnd.RecordedAt - stopStart.RecordedAt).TotalSeconds;

                if (stopDuration >= minStopDurationSeconds)
                {
                    sequenceNumber++;
                    var stopAddress = await _geocodingService.ReverseGeocodeAsync(
                        stopStart.Latitude, stopStart.Longitude);

                    activities.Add(new ActivitySegmentDto
                    {
                        Type = "stop",
                        SequenceNumber = sequenceNumber,
                        StartTime = stopStart.RecordedAt,
                        EndTime = stopEnd.RecordedAt,
                        DurationSeconds = stopDuration,
                        DurationFormatted = FormatDuration(stopDuration),
                        StartLocation = new LocationDto
                        {
                            Latitude = stopStart.Latitude,
                            Longitude = stopStart.Longitude,
                            Address = stopAddress ?? stopStart.Address
                        }
                    });
                }
            }
        }

        // === MERGE PHASE ===
        // Merge drive-shortStop-drive into one continuous drive (handles traffic lights < 3 min)
        // Merge consecutive stops into one
        const int MIN_STOP_BREAK_SECONDS = 180; // 3 minutes
        var merged = new List<ActivitySegmentDto>();

        for (int m = 0; m < activities.Count; m++)
        {
            var seg = activities[m];

            // Short stop between two drives → absorb into previous drive
            if (seg.Type == "stop" && merged.Count > 0 && m + 1 < activities.Count)
            {
                var prevSeg = merged[^1];
                var nextSeg = activities[m + 1];
                if (prevSeg.Type == "drive" && nextSeg.Type == "drive" && seg.DurationSeconds < MIN_STOP_BREAK_SECONDS)
                {
                    prevSeg.EndTime = nextSeg.EndTime;
                    prevSeg.EndLocation = nextSeg.EndLocation;
                    prevSeg.DurationSeconds += seg.DurationSeconds + nextSeg.DurationSeconds;
                    prevSeg.DurationFormatted = FormatDuration(prevSeg.DurationSeconds);
                    prevSeg.DistanceKm = (prevSeg.DistanceKm ?? 0) + (nextSeg.DistanceKm ?? 0);
                    prevSeg.MaxSpeedKph = Math.Max(prevSeg.MaxSpeedKph ?? 0, nextSeg.MaxSpeedKph ?? 0);
                    if (prevSeg.AvgSpeedKph.HasValue && nextSeg.AvgSpeedKph.HasValue)
                        prevSeg.AvgSpeedKph = Math.Round((prevSeg.AvgSpeedKph.Value + nextSeg.AvgSpeedKph.Value) / 2, 1);
                    m++; // Skip next drive (already merged)
                    continue;
                }
            }

            // Merge consecutive stops
            if (seg.Type == "stop" && merged.Count > 0 && merged[^1].Type == "stop")
            {
                var prevStop = merged[^1];
                prevStop.EndTime = seg.EndTime;
                prevStop.DurationSeconds += seg.DurationSeconds;
                prevStop.DurationFormatted = FormatDuration(prevStop.DurationSeconds);
                continue;
            }

            merged.Add(seg);
        }

        // Re-number sequences
        for (int m = 0; m < merged.Count; m++)
            merged[m].SequenceNumber = m + 1;

        report.Activities = merged;

        // Calculate summary from merged activities
        var drives = merged.Where(a => a.Type == "drive").ToList();
        var stops = merged.Where(a => a.Type == "stop").ToList();

        var totalDrivingSeconds = drives.Sum(d => d.DurationSeconds);
        var totalStoppedSeconds = stops.Sum(s => s.DurationSeconds);
        var totalActiveSeconds = totalDrivingSeconds + totalStoppedSeconds;

        report.Summary = new DailySummaryDto
        {
            TotalActiveSeconds = totalActiveSeconds,
            TotalDrivingSeconds = totalDrivingSeconds,
            TotalStoppedSeconds = totalStoppedSeconds,
            TotalActiveFormatted = FormatDuration(totalActiveSeconds),
            TotalDrivingFormatted = FormatDuration(totalDrivingSeconds),
            TotalStoppedFormatted = FormatDuration(totalStoppedSeconds),
            TotalDistanceKm = Math.Round(drives.Sum(d => d.DistanceKm ?? 0), 2),
            StopCount = stops.Count,
            DriveCount = drives.Count,
            MaxSpeedKph = drives.Any() ? drives.Max(d => d.MaxSpeedKph ?? 0) : 0,
            AvgSpeedKph = drives.Any() ? Math.Round(drives.Average(d => d.AvgSpeedKph ?? 0), 1) : 0,
            PositionCount = positions.Count
        };

        return report;
    }

    private static double CalculateDistance(List<GpsPosition> positions)
    {
        double totalDistance = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            totalDistance += HaversineDistance(
                positions[i - 1].Latitude, positions[i - 1].Longitude,
                positions[i].Latitude, positions[i].Longitude);
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

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";

        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        var secs = seconds % 60;

        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";

        return secs > 0 ? $"{minutes}m {secs}s" : $"{minutes}m";
    }
}

public class GetDailyActivityReportsQueryHandler : IRequestHandler<GetDailyActivityReportsQuery, List<DailyActivityReportDto>>
{
    private readonly IGisDbContext _context;
    private readonly IMediator _mediator;

    public GetDailyActivityReportsQueryHandler(IGisDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<List<DailyActivityReportDto>> Handle(GetDailyActivityReportsQuery request, CancellationToken ct)
    {
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.GpsDeviceId.HasValue);

        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicleIds = await vehiclesQuery.Select(v => v.Id).ToListAsync(ct);
        var reports = new List<DailyActivityReportDto>();

        foreach (var vehicleId in vehicleIds)
        {
            var report = await _mediator.Send(new GetDailyActivityReportQuery(
                vehicleId,
                request.Date,
                request.MinStopDurationSeconds,
                request.StopSpeedThresholdKph), ct);
            reports.Add(report);
        }

        return reports;
    }
}




