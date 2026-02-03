using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

public class GetFuelExpenseStatisticsQueryHandler : IRequestHandler<GetFuelExpenseStatisticsQuery, FleetFuelStatisticsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetFuelExpenseStatisticsQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<FleetFuelStatisticsDto> Handle(GetFuelExpenseStatisticsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;

        // Get vehicles with GPS devices
        var vehiclesQuery = _context.Vehicles
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue);

        if (request.VehicleId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.Id == request.VehicleId.Value);

        if (!string.IsNullOrEmpty(request.FuelType))
            vehiclesQuery = vehiclesQuery.Where(v => v.FuelType == request.FuelType);

        var vehicles = await vehiclesQuery
            .Include(v => v.GpsDevice)
            .ToListAsync(cancellationToken);

        // Get current fuel prices
        var fuelPrices = await GetCurrentFuelPricesAsync(companyId, cancellationToken);

        var vehicleExpenses = new List<VehicleFuelExpenseDto>();

        foreach (var vehicle in vehicles)
        {
            var expense = await _fuelCalculationService.CalculateVehicleFuelExpenseAsync(
                vehicle, startDate, endDate, fuelPrices, cancellationToken);
            
            if (expense != null)
                vehicleExpenses.Add(expense);
        }

        // Calculate fleet statistics
        var fleetStats = CalculateFleetStatistics(vehicleExpenses, fuelPrices);

        // Calculate monthly trends
        var monthlyTrends = await CalculateMonthlyTrendsAsync(
            companyId, startDate, endDate, fuelPrices, cancellationToken);

        return new FleetFuelStatisticsDto(
            TotalFleetFuelCost: fleetStats.TotalCost,
            TotalFleetFuelConsumedLiters: fleetStats.TotalFuel,
            FleetAverageConsumptionPer100Km: fleetStats.AverageConsumption,
            FleetStandardDeviation: fleetStats.StandardDeviation,
            TotalFleetDistanceKm: fleetStats.TotalDistance,
            VehicleCount: vehicleExpenses.Count,
            FuelTypeDistribution: fleetStats.FuelTypeDistribution,
            MonthlyTrends: monthlyTrends,
            VehicleExpenses: vehicleExpenses
        );
    }

    private async Task<Dictionary<string, decimal>> GetCurrentFuelPricesAsync(
        int companyId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        var prices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now &&
                         (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes,
                fp => fp.FuelTypeId,
                ft => ft.Id,
                (fp, ft) => new { ft.Code, fp.PricePerLiter })
            .ToListAsync(cancellationToken);

        return prices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);
    }

    private (decimal TotalCost, decimal TotalFuel, decimal AverageConsumption, 
             decimal StandardDeviation, int TotalDistance, 
             List<FuelTypeDistributionDto> FuelTypeDistribution) 
        CalculateFleetStatistics(List<VehicleFuelExpenseDto> expenses, Dictionary<string, decimal> fuelPrices)
    {
        if (!expenses.Any())
            return (0, 0, 0, 0, 0, new List<FuelTypeDistributionDto>());

        var totalCost = expenses.Sum(e => e.TotalFuelCost);
        var totalFuel = expenses.Sum(e => e.TotalFuelConsumedLiters);
        var totalDistance = expenses.Sum(e => e.TotalDistanceKm);

        var avgConsumption = totalDistance > 0 
            ? (totalFuel / totalDistance) * 100 
            : 0;

        // Calculate standard deviation
        var consumptions = expenses
            .Where(e => e.TotalDistanceKm > 0)
            .Select(e => e.AverageConsumptionPer100Km)
            .ToList();

        decimal stdDev = 0;
        if (consumptions.Count > 1)
        {
            var mean = consumptions.Average();
            var sumSquaredDiff = consumptions.Sum(c => (c - mean) * (c - mean));
            stdDev = (decimal)Math.Sqrt((double)(sumSquaredDiff / consumptions.Count));
        }

        // Fuel type distribution
        var distribution = expenses
            .Where(e => !string.IsNullOrEmpty(e.FuelType))
            .GroupBy(e => e.FuelType!)
            .Select(g => new FuelTypeDistributionDto(
                FuelType: g.Key,
                VehicleCount: g.Count(),
                TotalFuelConsumed: g.Sum(e => e.TotalFuelConsumedLiters),
                TotalCost: g.Sum(e => e.TotalFuelCost),
                Percentage: totalFuel > 0 
                    ? Math.Round(g.Sum(e => e.TotalFuelConsumedLiters) / totalFuel * 100, 2)
                    : 0
            ))
            .ToList();

        return (totalCost, totalFuel, avgConsumption, stdDev, totalDistance, distribution);
    }

    private async Task<List<MonthlyFuelTrendDto>> CalculateMonthlyTrendsAsync(
        int companyId, DateTime startDate, DateTime endDate,
        Dictionary<string, decimal> fuelPrices, CancellationToken cancellationToken)
    {
        var trends = new List<MonthlyFuelTrendDto>();
        var culture = new CultureInfo("fr-FR");

        var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
        var lastDate = new DateTime(endDate.Year, endDate.Month, 1);

        while (currentDate <= lastDate)
        {
            var monthStart = currentDate;
            var monthEnd = currentDate.AddMonths(1);

            // This is a placeholder - actual implementation would query GPS data
            trends.Add(new MonthlyFuelTrendDto(
                Year: currentDate.Year,
                Month: currentDate.Month,
                MonthName: culture.DateTimeFormat.GetMonthName(currentDate.Month),
                TotalFuelConsumed: 0,
                TotalCost: 0,
                AverageConsumption: 0
            ));

            currentDate = currentDate.AddMonths(1);
        }

        return trends;
    }
}

public class GetVehicleFuelExpenseQueryHandler : IRequestHandler<GetVehicleFuelExpenseQuery, VehicleFuelExpenseDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetVehicleFuelExpenseQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<VehicleFuelExpenseDto?> Handle(GetVehicleFuelExpenseQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;

        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, cancellationToken);

        if (vehicle == null || !vehicle.GpsDeviceId.HasValue)
            return null;

        var fuelPrices = await GetCurrentFuelPricesAsync(companyId, cancellationToken);

        return await _fuelCalculationService.CalculateVehicleFuelExpenseAsync(
            vehicle, startDate, endDate, fuelPrices, cancellationToken);
    }

    private async Task<Dictionary<string, decimal>> GetCurrentFuelPricesAsync(
        int companyId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        var prices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now &&
                         (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes,
                fp => fp.FuelTypeId,
                ft => ft.Id,
                (fp, ft) => new { ft.Code, fp.PricePerLiter })
            .ToListAsync(cancellationToken);

        return prices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);
    }
}

public class GetCurrentFuelPricesQueryHandler : IRequestHandler<GetCurrentFuelPricesQuery, List<FuelPriceDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetCurrentFuelPricesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<FuelPriceDto>> Handle(GetCurrentFuelPricesQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");
        var now = DateTime.UtcNow;

        var prices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now &&
                         (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes,
                fp => fp.FuelTypeId,
                ft => ft.Id,
                (fp, ft) => new FuelPriceDto(
                    ft.Id,
                    ft.Code,
                    ft.Name,
                    fp.PricePerLiter,
                    fp.EffectiveFrom
                ))
            .ToListAsync(cancellationToken);

        return prices;
    }
}
