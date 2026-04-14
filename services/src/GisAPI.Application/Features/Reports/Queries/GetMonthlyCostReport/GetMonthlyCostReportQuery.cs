using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;

public record GetMonthlyCostReportQuery(
    int Year,
    int Month,
    int? DepartmentId = null
) : IQuery<MonthlyCostReportDto>;

// ==================== REPORT DTOs ====================

public class MonthlyCostReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    // Summary totals
    public decimal TotalKm { get; set; }
    public decimal TotalFuelCostDzd { get; set; }
    public decimal TotalFuelLiters { get; set; }
    public decimal TotalMaintenanceCostDzd { get; set; }
    public decimal TotalRepairCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    // Per-vehicle detail rows grouped by department
    public List<DepartmentCostGroupDto> Departments { get; set; } = new();

    // Flat list of all vehicle rows (for table display)
    public List<VehicleMonthlyCostDto> Vehicles { get; set; } = new();
}

public class DepartmentCostGroupDto
{
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "Non assigné";

    // Department subtotals
    public decimal TotalKm { get; set; }
    public decimal TotalFuelCostDzd { get; set; }
    public decimal TotalFuelLiters { get; set; }
    public decimal TotalMaintenanceCostDzd { get; set; }
    public decimal TotalRepairCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    public List<VehicleMonthlyCostDto> Vehicles { get; set; } = new();
}

public class VehicleMonthlyCostDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public int? DepartmentId { get; set; }
    public string DepartmentName { get; set; } = "Non assigné";

    // Mileage
    public decimal Km { get; set; }
    // For fuel consumption report: KM PR = KM from GPS between fuel entries
    public decimal KmPr { get; set; }

    // Costs in DZD
    public decimal FuelCostDzd { get; set; }
    public decimal MaintenanceCostDzd { get; set; }
    public decimal RepairCostDzd { get; set; }
    public decimal TotalCostDzd { get; set; }

    // Fuel consumption (liters)
    public decimal FuelLiters { get; set; }
    // Liters from GPS-period (between refuels)
    public decimal FuelLitersPr { get; set; }

    // Computed ratios
    public decimal CostPerKm { get; set; }                // TotalCost / Km
    public decimal FuelPer100Km { get; set; }              // (FuelCostDzd / Km) * 100
    public decimal MaintenanceRepairPer100Km { get; set; } // ((Maint+Repair) / Km) * 100

    // Fuel consumption ratios
    public decimal ConsumptionPer100Km { get; set; }       // (FuelLiters / Km) * 100
    public decimal ConsumptionPrPer100Km { get; set; }     // (FuelLitersPr / KmPr) * 100
}
