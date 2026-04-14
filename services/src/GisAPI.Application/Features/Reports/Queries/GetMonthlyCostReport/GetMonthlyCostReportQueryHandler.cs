using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;

public class GetMonthlyCostReportQueryHandler : IRequestHandler<GetMonthlyCostReportQuery, MonthlyCostReportDto>
{
    private readonly IGisDbContext _context;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMonthlyCostReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<MonthlyCostReportDto> Handle(GetMonthlyCostReportQuery request, CancellationToken ct)
    {
        var startDate = DateTime.SpecifyKind(new DateTime(request.Year, request.Month, 1), DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(startDate.AddMonths(1), DateTimeKind.Utc);

        // 1. Fetch all vehicles with their departments and drivers
        var vehiclesQuery = _context.Vehicles.AsNoTracking()
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

        // 2. Fetch fuel entries for the month
        var fuelEntries = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startDate
                     && f.InvoiceDate < endDate)
            .ToListAsync(ct);

        // 3. Fetch maintenance records for the month
        var maintenanceRecords = await _context.MaintenanceRecords.AsNoTracking()
            .Where(m => vehicleIds.Contains(m.VehicleId)
                     && m.Date >= startDate
                     && m.Date < endDate)
            .ToListAsync(ct);

        // 4. Fetch repairs for the month
        var repairs = await _context.Repairs.AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId)
                     && r.RepairDate >= startDate
                     && r.RepairDate < endDate)
            .ToListAsync(ct);

        // 5. Compute mileage from GPS odometer readings (start of month vs end of month)
        var deviceVehicleMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v.Id);
        var deviceIds = deviceVehicleMap.Keys.ToList();

        // Get first and last odometer readings per device for the month
        var mileagePerVehicle = new Dictionary<int, decimal>();

        if (deviceIds.Any())
        {
            // Get start-of-month odometer for each device
            var startOdos = await _context.GpsPositions.AsNoTracking()
                .Where(p => deviceIds.Contains(p.DeviceId)
                         && p.RecordedAt >= startDate && p.RecordedAt < endDate
                         && p.OdometerKm.HasValue && p.OdometerKm > 0 && p.OdometerKm != 1048574)
                .GroupBy(p => p.DeviceId)
                .Select(g => new
                {
                    DeviceId = g.Key,
                    FirstOdo = g.OrderBy(p => p.RecordedAt).Select(p => p.OdometerKm).FirstOrDefault(),
                    LastOdo = g.OrderByDescending(p => p.RecordedAt).Select(p => p.OdometerKm).FirstOrDefault()
                })
                .ToListAsync(ct);

            foreach (var odo in startOdos)
            {
                if (deviceVehicleMap.TryGetValue(odo.DeviceId, out var vehicleId)
                    && odo.FirstOdo.HasValue && odo.LastOdo.HasValue)
                {
                    var km = Math.Max(0, odo.LastOdo.Value - odo.FirstOdo.Value);
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
        var maintByVehicle = maintenanceRecords
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.TotalCost));

        // 8. Group repairs by vehicle
        var repairByVehicle = repairs
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalCost));

        // 9. Build per-vehicle rows
        var vehicleRows = new List<VehicleMonthlyCostDto>();

        foreach (var vehicle in vehicles)
        {
            var km = mileagePerVehicle.GetValueOrDefault(vehicle.Id, 0);
            var fuelCost = fuelByVehicle.TryGetValue(vehicle.Id, out var fuel) ? fuel.TotalCost : 0;
            var fuelLiters = fuel?.TotalLiters ?? 0;
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
