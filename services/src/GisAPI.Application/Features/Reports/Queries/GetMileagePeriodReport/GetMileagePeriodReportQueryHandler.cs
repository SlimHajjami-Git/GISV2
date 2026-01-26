using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMileagePeriodReport;

public class GetMileagePeriodReportQueryHandler : IRequestHandler<GetMileagePeriodReportQuery, MileagePeriodReportDto>
{
    private readonly IGisDbContext _context;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMileagePeriodReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<MileagePeriodReportDto> Handle(GetMileagePeriodReportQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);

        if (vehicle == null)
        {
            return new MileagePeriodReportDto
            {
                VehicleId = request.VehicleId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                PeriodType = request.PeriodType,
                HasData = false
            };
        }

        if (!vehicle.GpsDeviceId.HasValue)
        {
            return new MileagePeriodReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.Name,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                PeriodType = request.PeriodType,
                HasData = false
            };
        }

        // Adjust for timezone offset (Tunisia/Morocco = UTC+1) and ensure UTC Kind
        var startDate = DateTime.SpecifyKind(request.StartDate.Date.AddHours(1), DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1).AddHours(1), DateTimeKind.Utc);

        // Extract device ID as non-nullable int for proper EF Core query translation
        int deviceId = vehicle.GpsDeviceId!.Value;
        
        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId &&
                        p.RecordedAt >= startDate &&
                        p.RecordedAt < endDate)
            .OrderBy(p => p.RecordedAt)
            .ToListAsync(ct);

        if (!positions.Any())
        {
            return new MileagePeriodReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.Name,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                PeriodType = request.PeriodType,
                HasData = false
            };
        }

        return request.PeriodType switch
        {
            MileagePeriodType.Hour => ProcessHourlyReport(vehicle, request, positions),
            MileagePeriodType.Day => ProcessDailyReport(vehicle, request, positions),
            MileagePeriodType.Month => ProcessMonthlyReport(vehicle, request, positions),
            _ => throw new ArgumentOutOfRangeException(nameof(request.PeriodType))
        };
    }

    private MileagePeriodReportDto ProcessHourlyReport(Vehicle vehicle, GetMileagePeriodReportQuery request, List<GpsPosition> positions)
    {
        var report = CreateBaseReport(vehicle, request);

        // Group by hour of day
        var hourlyGroups = positions
            .GroupBy(p => p.RecordedAt.AddHours(-1).Hour)
            .OrderBy(g => g.Key)
            .ToList();

        // Create 24-hour breakdown (fill missing hours with zeros)
        var hourlyBreakdown = new List<HourlyMileageDto>();
        for (int hour = 0; hour < 24; hour++)
        {
            var hourGroup = hourlyGroups.FirstOrDefault(g => g.Key == hour);
            if (hourGroup != null)
            {
                var hourPositions = hourGroup.OrderBy(p => p.RecordedAt).ToList();
                var distance = CalculateTotalDistance(hourPositions);
                var tripCount = CountTrips(hourPositions);
                var drivingMinutes = CalculateDrivingMinutes(hourPositions);
                var speeds = hourPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();

                hourlyBreakdown.Add(new HourlyMileageDto
                {
                    Hour = hour,
                    HourLabel = $"{hour:D2}:00",
                    DistanceKm = Math.Round(distance, 2),
                    TripCount = tripCount,
                    DrivingMinutes = drivingMinutes,
                    MaxSpeedKph = speeds.Any() ? Math.Round(speeds.Max(), 1) : 0,
                    AvgSpeedKph = speeds.Any() ? Math.Round(speeds.Average(), 1) : 0
                });
            }
            else
            {
                hourlyBreakdown.Add(new HourlyMileageDto
                {
                    Hour = hour,
                    HourLabel = $"{hour:D2}:00",
                    DistanceKm = 0,
                    TripCount = 0,
                    DrivingMinutes = 0,
                    MaxSpeedKph = 0,
                    AvgSpeedKph = 0
                });
            }
        }

        report.HourlyBreakdown = hourlyBreakdown;
        report.TotalDistanceKm = Math.Round(hourlyBreakdown.Sum(h => h.DistanceKm), 2);
        report.TotalTripCount = hourlyBreakdown.Sum(h => h.TripCount);
        report.TotalDrivingMinutes = hourlyBreakdown.Sum(h => h.DrivingMinutes);
        report.TotalDrivingFormatted = FormatDuration(report.TotalDrivingMinutes * 60);

        var activeHours = hourlyBreakdown.Where(h => h.DistanceKm > 0).ToList();
        report.AverageDistanceKm = activeHours.Any() ? Math.Round(activeHours.Average(h => h.DistanceKm), 2) : 0;
        report.MaxDistanceKm = hourlyBreakdown.Any() ? Math.Round(hourlyBreakdown.Max(h => h.DistanceKm), 2) : 0;
        report.MinDistanceKm = activeHours.Any() ? Math.Round(activeHours.Min(h => h.DistanceKm), 2) : 0;

        // Chart data
        report.ChartData = hourlyBreakdown.Select(h => new ChartDataPoint
        {
            Label = h.HourLabel,
            Value = h.DistanceKm,
            Tooltip = $"{h.DistanceKm:F1} km - {h.TripCount} trajets"
        }).ToList();

        return report;
    }

    private MileagePeriodReportDto ProcessDailyReport(Vehicle vehicle, GetMileagePeriodReportQuery request, List<GpsPosition> positions)
    {
        var report = CreateBaseReport(vehicle, request);

        // Group by date
        var dailyGroups = positions
            .GroupBy(p => p.RecordedAt.AddHours(-1).Date)
            .OrderBy(g => g.Key)
            .ToList();

        var dailyBreakdown = new List<DailyMileagePeriodDto>();
        
        // Fill all days in range
        var currentDate = request.StartDate.Date;
        while (currentDate <= request.EndDate.Date)
        {
            var dayGroup = dailyGroups.FirstOrDefault(g => g.Key == currentDate);
            if (dayGroup != null)
            {
                var dayPositions = dayGroup.OrderBy(p => p.RecordedAt).ToList();
                var distance = CalculateTotalDistance(dayPositions);
                var tripCount = CountTrips(dayPositions);
                var drivingMinutes = CalculateDrivingMinutes(dayPositions);
                var speeds = dayPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();

                dailyBreakdown.Add(new DailyMileagePeriodDto
                {
                    Date = currentDate,
                    DateLabel = currentDate.ToString("dd/MM/yyyy", FrenchCulture),
                    DayOfWeek = currentDate.ToString("dddd", FrenchCulture),
                    DistanceKm = Math.Round(distance, 2),
                    TripCount = tripCount,
                    DrivingMinutes = drivingMinutes,
                    MaxSpeedKph = speeds.Any() ? Math.Round(speeds.Max(), 1) : 0,
                    AvgSpeedKph = speeds.Any() ? Math.Round(speeds.Average(), 1) : 0
                });
            }
            else
            {
                dailyBreakdown.Add(new DailyMileagePeriodDto
                {
                    Date = currentDate,
                    DateLabel = currentDate.ToString("dd/MM/yyyy", FrenchCulture),
                    DayOfWeek = currentDate.ToString("dddd", FrenchCulture),
                    DistanceKm = 0,
                    TripCount = 0,
                    DrivingMinutes = 0,
                    MaxSpeedKph = 0,
                    AvgSpeedKph = 0
                });
            }
            currentDate = currentDate.AddDays(1);
        }

        report.DailyBreakdown = dailyBreakdown;
        report.TotalDistanceKm = Math.Round(dailyBreakdown.Sum(d => d.DistanceKm), 2);
        report.TotalTripCount = dailyBreakdown.Sum(d => d.TripCount);
        report.TotalDrivingMinutes = dailyBreakdown.Sum(d => d.DrivingMinutes);
        report.TotalDrivingFormatted = FormatDuration(report.TotalDrivingMinutes * 60);

        var activeDays = dailyBreakdown.Where(d => d.DistanceKm > 0).ToList();
        report.AverageDistanceKm = activeDays.Any() ? Math.Round(activeDays.Average(d => d.DistanceKm), 2) : 0;
        report.MaxDistanceKm = dailyBreakdown.Any() ? Math.Round(dailyBreakdown.Max(d => d.DistanceKm), 2) : 0;
        report.MinDistanceKm = activeDays.Any() ? Math.Round(activeDays.Min(d => d.DistanceKm), 2) : 0;

        // Chart data
        report.ChartData = dailyBreakdown.Select(d => new ChartDataPoint
        {
            Label = d.Date.ToString("dd/MM", FrenchCulture),
            Value = d.DistanceKm,
            Tooltip = $"{d.DayOfWeek}: {d.DistanceKm:F1} km"
        }).ToList();

        return report;
    }

    private MileagePeriodReportDto ProcessMonthlyReport(Vehicle vehicle, GetMileagePeriodReportQuery request, List<GpsPosition> positions)
    {
        var report = CreateBaseReport(vehicle, request);

        // Group by month
        var monthlyGroups = positions
            .GroupBy(p => new { p.RecordedAt.AddHours(-1).Year, p.RecordedAt.AddHours(-1).Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToList();

        var monthlyBreakdown = new List<MonthlyMileagePeriodDto>();
        
        // Fill all months in range
        var currentMonth = new DateTime(request.StartDate.Year, request.StartDate.Month, 1);
        var endMonth = new DateTime(request.EndDate.Year, request.EndDate.Month, 1);
        
        while (currentMonth <= endMonth)
        {
            var monthGroup = monthlyGroups.FirstOrDefault(g => g.Key.Year == currentMonth.Year && g.Key.Month == currentMonth.Month);
            var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
            
            if (monthGroup != null)
            {
                var monthPositions = monthGroup.OrderBy(p => p.RecordedAt).ToList();
                var distance = CalculateTotalDistance(monthPositions);
                var tripCount = CountTrips(monthPositions);
                var drivingMinutes = CalculateDrivingMinutes(monthPositions);
                
                // Count days with activity
                var daysWithActivity = monthPositions
                    .GroupBy(p => p.RecordedAt.AddHours(-1).Date)
                    .Count(g => CalculateTotalDistance(g.ToList()) > 0);

                monthlyBreakdown.Add(new MonthlyMileagePeriodDto
                {
                    Year = currentMonth.Year,
                    Month = currentMonth.Month,
                    MonthLabel = currentMonth.ToString("MMMM yyyy", FrenchCulture),
                    DistanceKm = Math.Round(distance, 2),
                    AverageDailyKm = daysWithActivity > 0 ? Math.Round(distance / daysWithActivity, 2) : 0,
                    TripCount = tripCount,
                    DrivingMinutes = drivingMinutes,
                    DaysWithActivity = daysWithActivity,
                    TotalDays = daysInMonth
                });
            }
            else
            {
                monthlyBreakdown.Add(new MonthlyMileagePeriodDto
                {
                    Year = currentMonth.Year,
                    Month = currentMonth.Month,
                    MonthLabel = currentMonth.ToString("MMMM yyyy", FrenchCulture),
                    DistanceKm = 0,
                    AverageDailyKm = 0,
                    TripCount = 0,
                    DrivingMinutes = 0,
                    DaysWithActivity = 0,
                    TotalDays = daysInMonth
                });
            }
            currentMonth = currentMonth.AddMonths(1);
        }

        report.MonthlyBreakdown = monthlyBreakdown;
        report.TotalDistanceKm = Math.Round(monthlyBreakdown.Sum(m => m.DistanceKm), 2);
        report.TotalTripCount = monthlyBreakdown.Sum(m => m.TripCount);
        report.TotalDrivingMinutes = monthlyBreakdown.Sum(m => m.DrivingMinutes);
        report.TotalDrivingFormatted = FormatDuration(report.TotalDrivingMinutes * 60);

        var activeMonths = monthlyBreakdown.Where(m => m.DistanceKm > 0).ToList();
        report.AverageDistanceKm = activeMonths.Any() ? Math.Round(activeMonths.Average(m => m.DistanceKm), 2) : 0;
        report.MaxDistanceKm = monthlyBreakdown.Any() ? Math.Round(monthlyBreakdown.Max(m => m.DistanceKm), 2) : 0;
        report.MinDistanceKm = activeMonths.Any() ? Math.Round(activeMonths.Min(m => m.DistanceKm), 2) : 0;

        // Chart data
        report.ChartData = monthlyBreakdown.Select(m => new ChartDataPoint
        {
            Label = new DateTime(m.Year, m.Month, 1).ToString("MMM yy", FrenchCulture),
            Value = m.DistanceKm,
            Tooltip = $"{m.MonthLabel}: {m.DistanceKm:F1} km ({m.DaysWithActivity} jours actifs)"
        }).ToList();

        return report;
    }

    private static MileagePeriodReportDto CreateBaseReport(Vehicle vehicle, GetMileagePeriodReportQuery request)
    {
        return new MileagePeriodReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            DriverName = vehicle.AssignedDriver?.Name,
            VehicleType = vehicle.Type,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PeriodType = request.PeriodType,
            HasData = true
        };
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
