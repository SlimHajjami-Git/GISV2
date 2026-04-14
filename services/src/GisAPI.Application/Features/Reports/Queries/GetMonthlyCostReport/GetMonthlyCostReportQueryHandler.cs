using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;

public class GetMonthlyCostReportQueryHandler : IRequestHandler<GetMonthlyCostReportQuery, MonthlyCostReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMonthlyCostReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<MonthlyCostReportDto> Handle(GetMonthlyCostReportQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var startDate = DateTime.SpecifyKind(new DateTime(request.Year, request.Month, 1), DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(startDate.AddMonths(1), DateTimeKind.Utc);

        // 1. Fetch vehicles scoped to current company
        // Explicit CompanyId filter (defense in depth; system admins also get scoped to their
        // active company for this report instead of seeing all fleets)
        var vehiclesQuery = _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.Department)
            .Include(v => v.AssignedDriver)
            .AsQueryable();

        if (request.DepartmentId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.DepartmentId == request.DepartmentId.Value);

        var vehicles = await vehiclesQuery.ToListAsync(ct);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        if (!vehicleIds.Any())
        {
            return new MonthlyCostReportDto
            {
                Year = request.Year,
                Month = request.Month,
                MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
                ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}",
                GeneratedAt = DateTime.UtcNow
            };
        }

        // 2. Fetch fuel entries from /carburant page's table (fuel_entries)
        var fuelEntries = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startDate
                     && f.InvoiceDate < endDate)
            .Select(f => new { f.VehicleId, f.TotalAmount, f.Volume })
            .ToListAsync(ct);

        // 3. Fetch maintenance logs from /entretien page's table (maintenance_logs)
        var maintenanceLogs = await _context.MaintenanceLogs.AsNoTracking()
            .Where(m => m.CompanyId == companyId
                     && vehicleIds.Contains(m.VehicleId)
                     && m.DoneDate >= startDate
                     && m.DoneDate < endDate)
            .Select(m => new { m.VehicleId, m.ActualCost })
            .ToListAsync(ct);

        // 4. Fetch repairs from /reparation page's table (repairs)
        var repairs = await _context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId
                     && vehicleIds.Contains(r.VehicleId)
                     && r.RepairDate >= startDate
                     && r.RepairDate < endDate)
            .Select(r => new { r.VehicleId, r.TotalCost })
            .ToListAsync(ct);

        // 5. Compute mileage from GPS odometer readings (start of month vs end of month)
        var deviceVehicleMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v.Id);
        var deviceIds = deviceVehicleMap.Keys.ToList();

        var mileagePerVehicle = new Dictionary<int, decimal>();

        if (deviceIds.Any())
        {
            // Use two separate simple queries instead of complex GroupBy+OrderBy+FirstOrDefault
            // which can fail in EF Core query translation on some PostgreSQL versions
            var odometerBase = _context.GpsPositions.AsNoTracking()
                .Where(p => deviceIds.Contains(p.DeviceId)
                         && p.RecordedAt >= startDate && p.RecordedAt < endDate
                         && p.OdometerKm.HasValue && p.OdometerKm > 0 && p.OdometerKm != 1048574);

            // First odometer per device (earliest in month)
            var firstOdos = await odometerBase
                .GroupBy(p => p.DeviceId)
                .Select(g => new { DeviceId = g.Key, Odo = g.Min(p => p.OdometerKm) })
                .ToListAsync(ct);

            // Last odometer per device (latest in month)
            var lastOdos = await odometerBase
                .GroupBy(p => p.DeviceId)
                .Select(g => new { DeviceId = g.Key, Odo = g.Max(p => p.OdometerKm) })
                .ToListAsync(ct);

            var firstMap = firstOdos.ToDictionary(x => x.DeviceId, x => x.Odo);
            var lastMap = lastOdos.ToDictionary(x => x.DeviceId, x => x.Odo);

            foreach (var deviceId in deviceIds)
            {
                if (firstMap.TryGetValue(deviceId, out var first) && first.HasValue
                    && lastMap.TryGetValue(deviceId, out var last) && last.HasValue
                    && deviceVehicleMap.TryGetValue(deviceId, out var vehicleId))
                {
                    var km = Math.Max(0, last.Value - first.Value);
                    mileagePerVehicle[vehicleId] = km;
                }
            }
        }

        // 6. Group fuel entries by vehicle
        var fuelByVehicle = fuelEntries
            .Where(f => f.VehicleId.HasValue)
            .GroupBy(f => f.VehicleId!.Value)
            .ToDictionary(g => g.Key, g => new
            {
                TotalCost = g.Sum(f => f.TotalAmount),
                TotalLiters = g.Sum(f => f.Volume)
            });

        // 7. Group maintenance by vehicle
        var maintByVehicle = maintenanceLogs
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.ActualCost));

        // 8. Group repairs by vehicle
        var repairByVehicle = repairs
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalCost));

        // 9. Build per-vehicle rows
        var vehicleRows = new List<VehicleMonthlyCostDto>();

        foreach (var vehicle in vehicles)
        {
            var km = mileagePerVehicle.GetValueOrDefault(vehicle.Id, 0);
            var hasFuel = fuelByVehicle.TryGetValue(vehicle.Id, out var fuel);
            var fuelCost = hasFuel ? fuel!.TotalCost : 0;
            var fuelLiters = hasFuel ? fuel!.TotalLiters : 0;
            var maintCost = maintByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var repairCost = repairByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var totalCost = fuelCost + maintCost + repairCost;

            // Only include vehicles with some activity
            if (km == 0 && fuelCost == 0 && maintCost == 0 && repairCost == 0)
                continue;

            var row = new VehicleMonthlyCostDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name ?? $"{vehicle.Brand} {vehicle.Model}".Trim(),
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                DepartmentId = vehicle.DepartmentId,
                DepartmentName = vehicle.Department?.Name ?? "Non assigné",
                Km = km,
                KmPr = km, // Same as GPS KM for now
                FuelCostDzd = fuelCost,
                FuelLiters = fuelLiters,
                FuelLitersPr = fuelLiters, // Same for now
                MaintenanceCostDzd = maintCost,
                RepairCostDzd = repairCost,
                TotalCostDzd = totalCost,
                CostPerKm = km > 0 ? Math.Round(totalCost / km, 2) : 0,
                FuelPer100Km = km > 0 ? Math.Round((fuelCost / km) * 100, 2) : 0,
                MaintenanceRepairPer100Km = km > 0 ? Math.Round(((maintCost + repairCost) / km) * 100, 2) : 0,
                ConsumptionPer100Km = km > 0 ? Math.Round((fuelLiters / km) * 100, 2) : 0,
                ConsumptionPrPer100Km = km > 0 ? Math.Round((fuelLiters / km) * 100, 2) : 0
            };
            vehicleRows.Add(row);
        }

        // 10. Group by department
        var departmentGroups = vehicleRows
            .GroupBy(v => new { v.DepartmentId, v.DepartmentName })
            .OrderBy(g => g.Key.DepartmentName)
            .Select(g => new DepartmentCostGroupDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.DepartmentName,
                TotalKm = g.Sum(v => v.Km),
                TotalFuelCostDzd = g.Sum(v => v.FuelCostDzd),
                TotalFuelLiters = g.Sum(v => v.FuelLiters),
                TotalMaintenanceCostDzd = g.Sum(v => v.MaintenanceCostDzd),
                TotalRepairCostDzd = g.Sum(v => v.RepairCostDzd),
                TotalCostDzd = g.Sum(v => v.TotalCostDzd),
                Vehicles = g.OrderBy(v => v.VehicleName).ToList()
            })
            .ToList();

        // 11. Build final report
        return new MonthlyCostReportDto
        {
            Year = request.Year,
            Month = request.Month,
            MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
            ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}",
            GeneratedAt = DateTime.UtcNow,
            TotalKm = vehicleRows.Sum(v => v.Km),
            TotalFuelCostDzd = vehicleRows.Sum(v => v.FuelCostDzd),
            TotalFuelLiters = vehicleRows.Sum(v => v.FuelLiters),
            TotalMaintenanceCostDzd = vehicleRows.Sum(v => v.MaintenanceCostDzd),
            TotalRepairCostDzd = vehicleRows.Sum(v => v.RepairCostDzd),
            TotalCostDzd = vehicleRows.Sum(v => v.TotalCostDzd),
            Departments = departmentGroups,
            Vehicles = vehicleRows.OrderBy(v => v.DepartmentName).ThenBy(v => v.VehicleName).ToList()
        };
    }
}
