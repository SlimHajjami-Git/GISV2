using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMileageReport;

public class GetMileageReportQueryHandler : IRequestHandler<GetMileageReportQuery, MileageReportDto>
{
    private readonly IGisDbContext _context;

    public GetMileageReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<MileageReportDto> Handle(GetMileageReportQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);

        if (vehicle == null)
        {
            return new MileageReportDto
            {
                VehicleId = request.VehicleId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HasData = false
            };
        }

        if (!vehicle.GpsDeviceId.HasValue)
        {
            return new MileageReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.Name,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HasData = false
            };
        }

        // Adjust for timezone offset (Tunisia = UTC+1)
        var startDate = request.StartDate.Date.AddHours(1);
        var endDate = request.EndDate.Date.AddDays(1).AddHours(1);

        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= startDate &&
                        p.RecordedAt < endDate)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(ct);

        if (!positions.Any())
        {
            return new MileageReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.Name,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HasData = false
            };
        }

        // Get previous period for comparison
        var periodDays = (request.EndDate - request.StartDate).Days + 1;
        var previousStart = request.StartDate.AddDays(-periodDays).AddHours(1);
        var previousEnd = request.StartDate.AddHours(1);

        var previousPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= previousStart &&
                        p.RecordedAt < previousEnd)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(ct);

        return ProcessMileageReport(vehicle, request.StartDate, request.EndDate, positions, previousPositions);
    }

    private MileageReportDto ProcessMileageReport(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        List<GpsPosition> positions,
        List<GpsPosition> previousPositions)
    {
        var report = new MileageReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            DriverName = vehicle.AssignedDriver?.Name,
            VehicleType = vehicle.Type,
            StartDate = startDate,
            EndDate = endDate,
            HasData = true
        };

        // Odometer readings
        var firstWithOdometer = positions.FirstOrDefault(p => p.OdometerKm.HasValue && p.OdometerKm > 0);
        var lastWithOdometer = positions.LastOrDefault(p => p.OdometerKm.HasValue && p.OdometerKm > 0);
        
        if (firstWithOdometer != null && lastWithOdometer != null)
        {
            report.StartOdometerKm = Math.Round((double)firstWithOdometer.OdometerKm!.Value, 2);
            report.EndOdometerKm = Math.Round((double)lastWithOdometer.OdometerKm!.Value, 2);
            report.OdometerDifferenceKm = Math.Round(report.EndOdometerKm.Value - report.StartOdometerKm.Value, 2);
        }

        // Calculate total distance from GPS positions
        report.TotalDistanceKm = CalculateTotalDistance(positions);

        // Group positions by day for daily breakdown
        var dailyGroups = positions
            .GroupBy(p => p.RecordedAt.AddHours(-1).Date)
            .OrderBy(g => g.Key)
            .ToList();

        var dailyBreakdown = new List<DailyMileageDto>();
        foreach (var dayGroup in dailyGroups)
        {
            var dayPositions = dayGroup.OrderBy(p => p.RecordedAt).ToList();
            var dayDistance = CalculateTotalDistance(dayPositions);
            var tripCount = CountTrips(dayPositions);
            var drivingMinutes = CalculateDrivingMinutes(dayPositions);
            var speeds = dayPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();

            var firstOdo = dayPositions.FirstOrDefault(p => p.OdometerKm > 0);
            var lastOdo = dayPositions.LastOrDefault(p => p.OdometerKm > 0);

            dailyBreakdown.Add(new DailyMileageDto
            {
                Date = dayGroup.Key,
                DayOfWeek = dayGroup.Key.ToString("dddd", new CultureInfo("fr-FR")),
                DistanceKm = Math.Round(dayDistance, 2),
                StartOdometerKm = firstOdo?.OdometerKm,
                EndOdometerKm = lastOdo?.OdometerKm,
                TripCount = tripCount,
                DrivingMinutes = drivingMinutes,
                MaxSpeedKph = speeds.Any() ? Math.Round(speeds.Max(), 1) : 0,
                AvgSpeedKph = speeds.Any() ? Math.Round(speeds.Average(), 1) : 0
            });
        }
        report.DailyBreakdown = dailyBreakdown;

        // Weekly breakdown
        var weeklyGroups = dailyBreakdown
            .GroupBy(d => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d.Date, CalendarWeekRule.FirstDay, DayOfWeek.Monday))
            .OrderBy(g => g.First().Date)
            .ToList();

        report.WeeklyBreakdown = weeklyGroups.Select(wg => new WeeklyMileageDto
        {
            WeekNumber = wg.Key,
            WeekStart = wg.Min(d => d.Date),
            WeekEnd = wg.Max(d => d.Date),
            DistanceKm = Math.Round(wg.Sum(d => d.DistanceKm), 2),
            AverageDailyKm = Math.Round(wg.Average(d => d.DistanceKm), 2),
            TripCount = wg.Sum(d => d.TripCount),
            DrivingMinutes = wg.Sum(d => d.DrivingMinutes)
        }).ToList();

        // Monthly breakdown
        var monthlyGroups = dailyBreakdown
            .GroupBy(d => new { d.Date.Year, d.Date.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToList();

        report.MonthlyBreakdown = monthlyGroups.Select(mg => new MonthlyMileageDto
        {
            Year = mg.Key.Year,
            Month = mg.Key.Month,
            MonthName = new DateTime(mg.Key.Year, mg.Key.Month, 1).ToString("MMMM yyyy", new CultureInfo("fr-FR")),
            DistanceKm = Math.Round(mg.Sum(d => d.DistanceKm), 2),
            AverageDailyKm = Math.Round(mg.Average(d => d.DistanceKm), 2),
            TripCount = mg.Sum(d => d.TripCount),
            DaysWithActivity = mg.Count(d => d.DistanceKm > 0)
        }).ToList();

        // Period comparison
        if (previousPositions.Any())
        {
            var previousDistance = CalculateTotalDistance(previousPositions);
            var difference = report.TotalDistanceKm - previousDistance;
            var percentChange = previousDistance > 0 
                ? ((report.TotalDistanceKm - previousDistance) / previousDistance) * 100 
                : 0;

            report.PreviousPeriodComparison = new PeriodComparisonDto
            {
                PreviousPeriodDistanceKm = Math.Round(previousDistance, 2),
                CurrentPeriodDistanceKm = Math.Round(report.TotalDistanceKm, 2),
                DifferenceKm = Math.Round(difference, 2),
                PercentageChange = Math.Round(percentChange, 1),
                Trend = percentChange > 5 ? "increase" : percentChange < -5 ? "decrease" : "stable"
            };
        }

        // Summary statistics
        var totalDays = (endDate - startDate).Days + 1;
        var daysWithActivity = dailyBreakdown.Count(d => d.DistanceKm > 0);
        var totalDrivingMinutes = dailyBreakdown.Sum(d => d.DrivingMinutes);
        var allSpeeds = positions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();

        var maxDaily = dailyBreakdown.Any() ? dailyBreakdown.Max(d => d.DistanceKm) : 0;
        var minDaily = dailyBreakdown.Where(d => d.DistanceKm > 0).DefaultIfEmpty().Min(d => d?.DistanceKm ?? 0);
        var maxDailyDate = dailyBreakdown.FirstOrDefault(d => d.DistanceKm == maxDaily)?.Date;
        var minDailyDate = dailyBreakdown.Where(d => d.DistanceKm > 0).FirstOrDefault(d => d.DistanceKm == minDaily)?.Date;

        report.Summary = new MileageSummaryDto
        {
            TotalDistanceKm = Math.Round(report.TotalDistanceKm, 2),
            AverageDailyKm = totalDays > 0 ? Math.Round(report.TotalDistanceKm / totalDays, 2) : 0,
            MaxDailyKm = Math.Round(maxDaily, 2),
            MinDailyKm = Math.Round(minDaily, 2),
            MaxDailyDate = maxDailyDate,
            MinDailyDate = minDailyDate,
            TotalTripCount = dailyBreakdown.Sum(d => d.TripCount),
            TotalDrivingMinutes = totalDrivingMinutes,
            TotalDrivingFormatted = FormatDuration(totalDrivingMinutes * 60),
            MaxSpeedKph = allSpeeds.Any() ? Math.Round(allSpeeds.Max(), 1) : 0,
            AvgSpeedKph = allSpeeds.Any() ? Math.Round(allSpeeds.Average(), 1) : 0,
            DaysWithActivity = daysWithActivity,
            TotalDays = totalDays,
            ActivityPercentage = totalDays > 0 ? Math.Round((double)daysWithActivity / totalDays * 100, 1) : 0
        };

        report.AverageDailyKm = report.Summary.AverageDailyKm;

        return report;
    }

    private static double CalculateTotalDistance(List<GpsPosition> positions)
    {
        double totalDistance = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            
            // Only count distance if there's movement
            if ((curr.SpeedKph ?? 0) > 0 || (prev.SpeedKph ?? 0) > 0)
            {
                totalDistance += HaversineDistance(
                    prev.Latitude, prev.Longitude,
                    curr.Latitude, curr.Longitude);
            }
        }
        return totalDistance;
    }

    private static int CountTrips(List<GpsPosition> positions)
    {
        int tripCount = 0;
        bool wasMoving = false;

        foreach (var pos in positions)
        {
            var isMoving = (pos.SpeedKph ?? 0) > 3.0 || (pos.IgnitionOn ?? false);
            
            if (isMoving && !wasMoving)
            {
                tripCount++;
            }
            wasMoving = isMoving;
        }

        return tripCount;
    }

    private static int CalculateDrivingMinutes(List<GpsPosition> positions)
    {
        if (positions.Count < 2) return 0;

        int totalSeconds = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];

            // Count time as driving if speed > 0 or ignition on
            if ((prev.SpeedKph ?? 0) > 0 || (prev.IgnitionOn ?? false))
            {
                var seconds = (int)(curr.RecordedAt - prev.RecordedAt).TotalSeconds;
                if (seconds > 0 && seconds < 600) // Max 10 minutes gap to consider continuous
                {
                    totalSeconds += seconds;
                }
            }
        }

        return totalSeconds / 60;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // Earth's radius in km
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

        if (hours > 0)
            return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";

        return $"{minutes}m";
    }
}

public class GetMileageReportsQueryHandler : IRequestHandler<GetMileageReportsQuery, List<MileageReportDto>>
{
    private readonly IGisDbContext _context;
    private readonly IMediator _mediator;

    public GetMileageReportsQueryHandler(IGisDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    public async Task<List<MileageReportDto>> Handle(GetMileageReportsQuery request, CancellationToken ct)
    {
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.GpsDeviceId.HasValue);

        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicleIds = await vehiclesQuery.Select(v => v.Id).ToListAsync(ct);
        var reports = new List<MileageReportDto>();

        foreach (var vehicleId in vehicleIds)
        {
            var report = await _mediator.Send(new GetMileageReportQuery(
                vehicleId,
                request.StartDate,
                request.EndDate), ct);
            reports.Add(report);
        }

        return reports;
    }
}
