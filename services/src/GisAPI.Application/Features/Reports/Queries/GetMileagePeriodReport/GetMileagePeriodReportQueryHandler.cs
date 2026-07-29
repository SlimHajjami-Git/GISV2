using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMileagePeriodReport;

public class GetMileagePeriodReportQueryHandler : IRequestHandler<GetMileagePeriodReportQuery, MileagePeriodReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(1); // Tunisia = UTC+1

    public GetMileagePeriodReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<MileagePeriodReportDto> Handle(GetMileagePeriodReportQuery request, CancellationToken ct)
    {
        // Écran opérationnel : on borne explicitement à la société de l'appelant.
        // Le filtre global de multi-tenance est contourné pour les administrateurs
        // système, sans ce Where un admin pourrait sortir le rapport kilométrique
        // d'un véhicule appartenant à une AUTRE société (fuite inter-sociétés).
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct);

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
                DriverName = vehicle.AssignedDriver?.FullName,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                PeriodType = request.PeriodType,
                HasData = false
            };
        }

        // DB stores local time directly (no timezone offset)
        var startDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1), DateTimeKind.Utc);

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
                DriverName = vehicle.AssignedDriver?.FullName,
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

        // Distance repartie par bucket sur la liste CONTINUE : la somme des heures
        // egale la distance reelle (aucun segment perdu aux frontieres d heure), donc
        // le total est identique quelle que soit la granularite choisie.
        var distByHour = GpsDistanceCalculator.CalculateBucketedDistanceKm(
            positions, p => (p.RecordedAt + LocalOffset).Hour);

        // Group by hour of day (adjusted to local time: UTC+1 for Tunisia)
        var hourlyGroups = positions
            .GroupBy(p => (p.RecordedAt + LocalOffset).Hour)
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
                var distance = distByHour.GetValueOrDefault(hour);
                var tripCount = CountTrips(hourPositions);
                var drivingMinutes = CalculateDrivingMinutes(hourPositions);
                var maxSpeed = hourPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();
                // Avg speed = distance / driving time (not average of GPS readings)
                var avgSpeedKph = drivingMinutes > 0 ? distance / (drivingMinutes / 60.0) : 0;

                hourlyBreakdown.Add(new HourlyMileageDto
                {
                    Hour = hour,
                    HourLabel = $"{hour:D2}:00",
                    DistanceKm = Math.Round(distance, 2),
                    TripCount = tripCount,
                    DrivingMinutes = drivingMinutes,
                    MaxSpeedKph = maxSpeed.Any() ? Math.Round(maxSpeed.Max(), 1) : 0,
                    AvgSpeedKph = Math.Round(avgSpeedKph, 1)
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

        // Distance repartie par jour sur la liste CONTINUE : la somme des jours egale
        // la distance reelle (aucun segment perdu au passage de minuit).
        var distByDate = GpsDistanceCalculator.CalculateBucketedDistanceKm(
            positions, p => (p.RecordedAt + LocalOffset).Date);

        // Group by date (adjusted to local time: UTC+1 for Tunisia)
        var dailyGroups = positions
            .GroupBy(p => (p.RecordedAt + LocalOffset).Date)
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
                var distance = distByDate.GetValueOrDefault(currentDate);
                var tripCount = CountTrips(dayPositions);
                var drivingMinutes = CalculateDrivingMinutes(dayPositions);
                var maxSpeed = dayPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();
                // Avg speed = distance / driving time (not average of GPS readings)
                var avgSpeedKph = drivingMinutes > 0 ? distance / (drivingMinutes / 60.0) : 0;

                dailyBreakdown.Add(new DailyMileagePeriodDto
                {
                    Date = currentDate,
                    DateLabel = currentDate.ToString("dd/MM/yyyy", FrenchCulture),
                    DayOfWeek = currentDate.ToString("dddd", FrenchCulture),
                    DistanceKm = Math.Round(distance, 2),
                    TripCount = tripCount,
                    DrivingMinutes = drivingMinutes,
                    MaxSpeedKph = maxSpeed.Any() ? Math.Round(maxSpeed.Max(), 1) : 0,
                    AvgSpeedKph = Math.Round(avgSpeedKph, 1)
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

        // Distance repartie par JOUR sur la liste CONTINUE. La distance d un mois =
        // somme de ses jours, et le total = somme de tous les jours : identique aux
        // vues heure/jour pour la meme fenetre (plus de segments perdus aux frontieres).
        var distByDate = GpsDistanceCalculator.CalculateBucketedDistanceKm(
            positions, p => (p.RecordedAt + LocalOffset).Date);

        // Group by month (adjusted to local time: UTC+1 for Tunisia)
        var monthlyGroups = positions
            .GroupBy(p => new { (p.RecordedAt + LocalOffset).Year, (p.RecordedAt + LocalOffset).Month })
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
                // Distance + jours actifs derives du bucketing JOURNALIER continu
                // (somme des jours = distance reelle du mois, coherente avec heure/jour).
                var monthDates = distByDate
                    .Where(kv => kv.Key.Year == currentMonth.Year && kv.Key.Month == currentMonth.Month)
                    .ToList();
                var distance = monthDates.Sum(kv => kv.Value);
                var tripCount = CountTrips(monthPositions);
                var drivingMinutes = CalculateDrivingMinutes(monthPositions);

                // Count days with activity
                var daysWithActivity = monthDates.Count(kv => kv.Value > 0);

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
            DriverName = vehicle.AssignedDriver?.FullName,
            VehicleType = vehicle.Type,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            PeriodType = request.PeriodType,
            HasData = true
        };
    }

    /// <summary>
    /// Calypso 8 — bug rapporte : "Kilometrage 0, trajets 5, vitesse max 8 km/h".
    /// Cause : la version precedente comptait un trajet a chaque transition
    /// vitesse <3 -> >3 km/h, sans verifier qu un deplacement reel ait eu lieu.
    /// Combine au filtre de distance qui ignore les segments < 5m (GPS jitter),
    /// on pouvait avoir 5 "trajets" comptes mais 0 km accumule (typique d un
    /// vehicule a l arret avec moteur tournant + jitter GPS qui depasse 3 km/h
    /// brievement).
    ///
    /// Fix : on tracke chaque segment "moving" et on ne le compte comme trajet
    /// que s il a accumule au moins 100m de distance reelle. On passe aussi le
    /// seuil de 3 a 5 km/h (aligne avec le reste du code, ex monitoring).
    /// </summary>
    private static int CountTrips(List<GpsPosition> positions)
    {
        const double MovingThresholdKph = 5.0;
        const double MinTripDistanceKm = 0.1; // 100m minimum reel pour compter un trajet
        const double MaxSegmentKm = 10.0;
        const double MinSegmentKm = 0.005;

        int tripCount = 0;
        bool inTrip = false;
        double currentTripKm = 0;

        for (int i = 0; i < positions.Count; i++)
        {
            var pos = positions[i];
            var isMoving = (pos.SpeedKph ?? 0) > MovingThresholdKph;

            if (isMoving && !inTrip)
            {
                inTrip = true;
                currentTripKm = 0;
            }
            else if (!isMoving && inTrip)
            {
                if (currentTripKm >= MinTripDistanceKm) tripCount++;
                inTrip = false;
                currentTripKm = 0;
            }

            if (inTrip && i > 0)
            {
                var prev = positions[i - 1];
                var segmentKm = GpsDistanceCalculator.HaversineKm(prev.Latitude, prev.Longitude, pos.Latitude, pos.Longitude);
                if (segmentKm >= MinSegmentKm && segmentKm <= MaxSegmentKm)
                {
                    currentTripKm += segmentKm;
                }
            }
        }

        // Trajet ouvert a la fin de la fenetre : on le compte aussi
        if (inTrip && currentTripKm >= MinTripDistanceKm) tripCount++;

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

            // Count time as driving only if vehicle is actually moving (speed > 2 km/h)
            if ((prev.SpeedKph ?? 0) > 2)
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

    // Haversine code factorise dans GpsDistanceCalculator (Common/Services).

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




