using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMileageReport;

public class GetMileageReportQueryHandler : IRequestHandler<GetMileageReportQuery, MileageReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly TimeSpan LocalOffset = TimeSpan.FromHours(1); // Tunisia = UTC+1

    public GetMileageReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<MileageReportDto> Handle(GetMileageReportQuery request, CancellationToken ct)
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
            // Véhicule SANS boîtier (offre GPA) : le kilométrage est dérivé des
            // relevés au compteur saisis à chaque plein (recette client du
            // 25/08/2026 — « les trois champs n'affichent rien »). On prend le
            // premier et le dernier relevé de la période ; leur écart est la
            // distance parcourue. Sans relevé (ou un seul), pas de distance
            // calculable -> HasData=false, comme avant.
            var sd = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
            var ed = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1), DateTimeKind.Utc);
            var readings = await _context.FuelEntries.AsNoTracking()
                .Where(f => f.VehicleId == request.VehicleId
                            && f.OdometerKm != null && f.OdometerKm > 0
                            && f.InvoiceDate >= sd && f.InvoiceDate < ed)
                .OrderBy(f => f.InvoiceDate)
                .Select(f => new { f.InvoiceDate, Km = f.OdometerKm!.Value })
                .ToListAsync(ct);

            var dto = new MileageReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HasData = readings.Count >= 2
            };

            if (readings.Count >= 2)
            {
                var start = (double)readings.First().Km;
                var end = (double)readings.Last().Km;
                var diff = Math.Max(0, end - start);
                dto.StartOdometerKm = Math.Round(start, 2);
                dto.EndOdometerKm = Math.Round(end, 2);
                dto.OdometerDifferenceKm = Math.Round(diff, 2);
                dto.TotalDistanceKm = Math.Round(diff, 2);
                var days = Math.Max(1, (request.EndDate.Date - request.StartDate.Date).Days + 1);
                dto.AverageDailyKm = Math.Round(diff / days, 2);
                dto.DailyBreakdown = readings.Select(r => new DailyMileageDto
                {
                    Date = r.InvoiceDate.Date,
                    EndOdometerKm = (double)r.Km
                }).ToList();
            }

            return dto;
        }

        // DB stores local time directly (no timezone offset)
        var startDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1), DateTimeKind.Utc);

        // Agrégation côté Postgres : voir MileageAggregation pour le détail.
        // Auparavant toutes les positions étaient rapatriées en mémoire, ce qui
        // provoquait une OutOfMemoryException sur les gros parcs.
        var deviceIds = new[] { vehicle.GpsDeviceId!.Value };
        var daily = await MileageAggregation.DailyAsync(_context, deviceIds, startDate, endDate, ct);

        if (daily.Count == 0)
        {
            return new MileageReportDto
            {
                VehicleId = request.VehicleId,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                VehicleType = vehicle.Type,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                HasData = false
            };
        }

        // Période de comparaison : une seule valeur agrégée suffit.
        var periodDays = (request.EndDate - request.StartDate).Days + 1;
        var previousStart = DateTime.SpecifyKind(request.StartDate.AddDays(-periodDays), DateTimeKind.Utc);
        var previousEnd = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);

        var previousTotals = await MileageAggregation.TotalsAsync(_context, deviceIds, previousStart, previousEnd, ct);
        var previousDistance = previousTotals.FirstOrDefault()?.DistanceKm;

        return BuildReport(vehicle, request.StartDate, request.EndDate, daily, previousDistance);
    }

    /// <summary>
    /// Assemble le rapport à partir des lignes journalières agrégées en SQL.
    /// Remplace l'ancien ProcessMileageReport, qui recalculait tout en mémoire
    /// à partir de la liste brute des positions.
    /// </summary>
    internal static MileageReportDto BuildReport(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        List<MileageAggregation.DailyRow> daily,
        double? previousDistanceKm)
    {
        var report = new MileageReportDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            DriverName = vehicle.AssignedDriver?.FullName,
            VehicleType = vehicle.Type,
            StartDate = startDate,
            EndDate = endDate,
            HasData = true
        };

        var ordered = daily.OrderBy(d => d.LocalDay).ToList();

        // Compteur sur la période : premier et dernier relevé valides trouvés.
        var firstOdo = ordered.FirstOrDefault(d => d.StartOdometerKm.HasValue)?.StartOdometerKm;
        var lastOdo = ordered.LastOrDefault(d => d.EndOdometerKm.HasValue)?.EndOdometerKm;
        if (firstOdo.HasValue && lastOdo.HasValue)
        {
            report.StartOdometerKm = Math.Round((double)firstOdo.Value, 2);
            report.EndOdometerKm = Math.Round((double)lastOdo.Value, 2);
            report.OdometerDifferenceKm = Math.Round(report.EndOdometerKm.Value - report.StartOdometerKm.Value, 2);
        }

        var dailyBreakdown = ordered.Select(d =>
        {
            var drivingMinutes = d.DrivingSeconds / 60;
            // Vitesse moyenne = distance / temps de conduite (plus juste qu'une
            // moyenne des relevés GPS, qui surpondère les points à l'arrêt).
            var avgSpeedKph = drivingMinutes > 0 ? d.DistanceKm / (drivingMinutes / 60.0) : 0;
            return new DailyMileageDto
            {
                Date = d.LocalDay,
                DayOfWeek = d.LocalDay.ToString("dddd", new CultureInfo("fr-FR")),
                DistanceKm = Math.Round(d.DistanceKm, 2),
                // La colonne Postgres est numeric : Npgsql la mappe en decimal,
                // le DTO expose des double.
                StartOdometerKm = (double?)d.StartOdometerKm,
                EndOdometerKm = (double?)d.EndOdometerKm,
                TripCount = d.TripCount,
                DrivingMinutes = drivingMinutes,
                MaxSpeedKph = Math.Round(d.MaxSpeedKph, 1),
                AvgSpeedKph = Math.Round(avgSpeedKph, 1)
            };
        }).ToList();

        report.DailyBreakdown = dailyBreakdown;
        report.TotalDistanceKm = dailyBreakdown.Sum(d => d.DistanceKm);

        // Weekly breakdown
        report.WeeklyBreakdown = dailyBreakdown
            .GroupBy(d => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d.Date, CalendarWeekRule.FirstDay, DayOfWeek.Monday))
            .OrderBy(g => g.First().Date)
            .Select(wg => new WeeklyMileageDto
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
        report.MonthlyBreakdown = dailyBreakdown
            .GroupBy(d => new { d.Date.Year, d.Date.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(mg => new MonthlyMileageDto
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
        if (previousDistanceKm.HasValue && previousDistanceKm.Value > 0)
        {
            var previousDistance = previousDistanceKm.Value;
            var difference = report.TotalDistanceKm - previousDistance;
            var percentChange = ((report.TotalDistanceKm - previousDistance) / previousDistance) * 100;

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
        var maxDaily = dailyBreakdown.Any() ? dailyBreakdown.Max(d => d.DistanceKm) : 0;
        var minDaily = dailyBreakdown.Where(d => d.DistanceKm > 0).Select(d => d.DistanceKm).DefaultIfEmpty().Min();
        var maxDailyDate = dailyBreakdown.FirstOrDefault(d => d.DistanceKm == maxDaily)?.Date;
        var minDailyDate = dailyBreakdown.Where(d => d.DistanceKm > 0).FirstOrDefault(d => d.DistanceKm == minDaily)?.Date;

        var summaryMaxSpeed = dailyBreakdown.Select(d => d.MaxSpeedKph).DefaultIfEmpty().Max();
        var summaryAvgSpeed = totalDrivingMinutes > 0 ? report.TotalDistanceKm / (totalDrivingMinutes / 60.0) : 0;

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
            MaxSpeedKph = Math.Round(summaryMaxSpeed, 1),
            AvgSpeedKph = Math.Round(summaryAvgSpeed, 1),
            DaysWithActivity = daysWithActivity,
            TotalDays = totalDays,
            ActivityPercentage = totalDays > 0 ? Math.Round((double)daysWithActivity / totalDays * 100, 1) : 0
        };

        report.AverageDailyKm = report.Summary.AverageDailyKm;

        return report;
    }

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
    private readonly ICurrentTenantService _tenantService;

    public GetMileageReportsQueryHandler(IGisDbContext context, IMediator mediator, ICurrentTenantService tenantService)
    {
        _context = context;
        _mediator = mediator;
        _tenantService = tenantService;
    }

    public async Task<List<MileageReportDto>> Handle(GetMileageReportsQuery request, CancellationToken ct)
    {
        // Écran opérationnel : sans ce filtre, le rapport multi-véhicules listait
        // tout le parc de TOUTES les sociétés pour un administrateur système
        // (le filtre global de multi-tenance est contourné pour ces comptes).
        var companyId = _tenantService.CompanyId ?? 0;

        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue);

        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicles = await vehiclesQuery.ToListAsync(ct);
        // NB : on ne sort plus si vehicles est vide — une société « sans GPS »
        // n'a que des véhicules sans boîtier, traités par le bloc dérivé en fin
        // de méthode. startDate/endDate sont déclarés ici car les deux blocs
        // (GPS et dérivé) les utilisent.
        var startDate = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(request.EndDate.Date.AddDays(1), DateTimeKind.Utc);

        var reports = new List<MileageReportDto>();
        if (vehicles.Count > 0)
        {
        // Auparavant : une requête MediatR PAR véhicule, chacune rapatriant
        // toutes ses positions (période courante + période de comparaison).
        // Sur un parc de 14 véhicules cela suffisait à saturer les 512 Mi du pod.
        // Désormais : DEUX requêtes agrégées pour l'ensemble du parc.
        var deviceIds = vehicles.Select(v => v.GpsDeviceId!.Value).Distinct().ToArray();

        var periodDays = (request.EndDate - request.StartDate).Days + 1;
        var previousStart = DateTime.SpecifyKind(request.StartDate.AddDays(-periodDays), DateTimeKind.Utc);
        var previousEnd = DateTime.SpecifyKind(request.StartDate.Date, DateTimeKind.Utc);

        var daily = await MileageAggregation.DailyAsync(_context, deviceIds, startDate, endDate, ct);
        var previousTotals = await MileageAggregation.TotalsAsync(_context, deviceIds, previousStart, previousEnd, ct);

        var dailyByDevice = daily.GroupBy(d => d.DeviceId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var previousByDevice = previousTotals.ToDictionary(t => t.DeviceId, t => t.DistanceKm);

        foreach (var vehicle in vehicles)
        {
            var deviceId = vehicle.GpsDeviceId!.Value;
            if (!dailyByDevice.TryGetValue(deviceId, out var rows) || rows.Count == 0)
            {
                // Aucune trame sur la période : même forme de réponse qu'avant.
                reports.Add(new MileageReportDto
                {
                    VehicleId = vehicle.Id,
                    VehicleName = vehicle.Name,
                    Plate = vehicle.Plate,
                    DriverName = vehicle.AssignedDriver?.FullName,
                    VehicleType = vehicle.Type,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    HasData = false
                });
                continue;
            }

            previousByDevice.TryGetValue(deviceId, out var prevKm);
            reports.Add(GetMileageReportQueryHandler.BuildReport(
                vehicle, request.StartDate, request.EndDate, rows,
                prevKm > 0 ? prevKm : null));
        }
        } // fin du traitement des véhicules AVEC boîtier

        // Véhicules SANS boîtier (offre GPA) : le kilométrage est dérivé des
        // relevés au compteur saisis aux pleins, comme pour le rapport d'un
        // véhicule unique. Sans ce bloc, le rapport « tous véhicules » d'une
        // société GPA serait vide (recette client du 25/08/2026).
        var noGpsQuery = _context.Vehicles.AsNoTracking()
            .Include(v => v.AssignedDriver)
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId == null);
        if (request.VehicleIds != null && request.VehicleIds.Length > 0)
            noGpsQuery = noGpsQuery.Where(v => request.VehicleIds.Contains(v.Id));
        var noGpsVehicles = await noGpsQuery.ToListAsync(ct);

        if (noGpsVehicles.Count > 0)
        {
            var noGpsIds = noGpsVehicles.Select(v => v.Id).ToList();
            var readingsByVehicle = (await _context.FuelEntries.AsNoTracking()
                .Where(f => f.VehicleId != null && noGpsIds.Contains(f.VehicleId.Value)
                            && f.OdometerKm != null && f.OdometerKm > 0
                            && f.InvoiceDate >= startDate && f.InvoiceDate < endDate)
                .Select(f => new { VehicleId = f.VehicleId!.Value, f.InvoiceDate, Km = f.OdometerKm!.Value })
                .ToListAsync(ct))
                .GroupBy(r => r.VehicleId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.InvoiceDate).ToList());

            var totalDays = Math.Max(1, (request.EndDate.Date - request.StartDate.Date).Days + 1);
            foreach (var vehicle in noGpsVehicles)
            {
                var dto = new MileageReportDto
                {
                    VehicleId = vehicle.Id,
                    VehicleName = vehicle.Name,
                    Plate = vehicle.Plate,
                    DriverName = vehicle.AssignedDriver?.FullName,
                    VehicleType = vehicle.Type,
                    StartDate = request.StartDate,
                    EndDate = request.EndDate,
                    HasData = false
                };
                if (readingsByVehicle.TryGetValue(vehicle.Id, out var rd) && rd.Count >= 2)
                {
                    var start = (double)rd.First().Km;
                    var end = (double)rd.Last().Km;
                    var diff = Math.Max(0, end - start);
                    dto.HasData = true;
                    dto.StartOdometerKm = Math.Round(start, 2);
                    dto.EndOdometerKm = Math.Round(end, 2);
                    dto.OdometerDifferenceKm = Math.Round(diff, 2);
                    dto.TotalDistanceKm = Math.Round(diff, 2);
                    dto.AverageDailyKm = Math.Round(diff / totalDays, 2);
                }
                reports.Add(dto);
            }
        }

        return reports;
    }
}




