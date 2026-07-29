using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

public class GetFuelComparisonReportQueryHandler
    : IRequestHandler<GetFuelComparisonReportQuery, FuelComparisonReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetFuelComparisonReportQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<FuelComparisonReportDto> Handle(GetFuelComparisonReportQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;

        // Portee vehicules : ce comparatif agrege litres et couts reels par vehicule. Le
        // filtre est applique AVANT l'agregation, sinon les totaux resteraient ceux du parc
        // entier. Fuite constatee : un employe restreint voyait les depenses de tout le parc.
        // scope == null => admin societe, aucun filtre supplementaire.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        // Company vehicles (all of them — a vehicle billed for fuel but with no GPS is itself
        // worth surfacing). Include the device so we can tell sensor vs estimation.
        var vq = _context.Vehicles.Where(v => v.CompanyId == companyId);
        if (request.VehicleId.HasValue)
            vq = vq.Where(v => v.Id == request.VehicleId.Value);
        if (scope is not null)
            vq = vq.Where(v => scope.Contains(v.Id));
        var vehicles = await vq.Include(v => v.GpsDevice).ToListAsync(ct);

        // REAL fuel billed in the period (card fills entered via /carburant).
        var feq = _context.FuelEntries
            .Where(fe => fe.CompanyId == companyId
                         && fe.InvoiceDate >= startDate
                         && fe.InvoiceDate <= endDate);

        if (scope is not null)
            // Les saisies non rattachees (VehicleId null) sont conservees : elles ne seront
            // rapprochees, plus bas, que des vehicules autorises via la plaque.
            feq = feq.Where(fe => fe.VehicleId == null || scope.Contains(fe.VehicleId.Value));

        var realEntries = await feq
            .Select(fe => new { fe.VehicleId, fe.VehiclePlate, fe.Volume, fe.TotalAmount })
            .ToListAsync(ct);

        var fuelPrices = await GetCurrentFuelPricesAsync(companyId, ct);

        var rows = new List<FuelComparisonRowDto>();

        foreach (var vehicle in vehicles)
        {
            // GPS / boitier side
            decimal gpsLiters = 0m;
            int distanceKm = 0;
            string source = "aucun";

            if (vehicle.GpsDeviceId.HasValue)
            {
                var gps = await _fuelCalculationService.CalculateVehicleFuelExpenseAsync(
                    vehicle, startDate, endDate, fuelPrices, ct);
                if (gps != null)
                {
                    gpsLiters = gps.TotalFuelConsumedLiters;
                    distanceKm = gps.TotalDistanceKm;
                    source = gps.IsEstimated ? "estime" : "capteur";
                }
                else
                {
                    source = "estime";
                }
            }

            // REAL side — match by VehicleId, or by plate for entries that were never linked.
            var matching = realEntries
                .Where(e => e.VehicleId == vehicle.Id
                            || (e.VehicleId == null
                                && !string.IsNullOrEmpty(vehicle.Plate)
                                && e.VehiclePlate == vehicle.Plate))
                .ToList();
            var realLiters = matching.Sum(e => e.Volume);
            var realCost = matching.Sum(e => e.TotalAmount);

            // Skip vehicles with no fuel activity at all on either side.
            if (realLiters == 0m && gpsLiters == 0m)
                continue;

            var diffLiters = realLiters - gpsLiters;
            var diffPercent = gpsLiters > 0m
                ? Math.Round(diffLiters / gpsLiters * 100m, 1)
                : (realLiters > 0m ? 100m : 0m);

            rows.Add(new FuelComparisonRowDto(
                vehicle.Id,
                string.IsNullOrWhiteSpace(vehicle.Name) ? (vehicle.Plate ?? $"Vehicule {vehicle.Id}") : vehicle.Name,
                vehicle.Plate,
                Math.Round(realLiters, 1),
                Math.Round(realCost, 2),
                Math.Round(gpsLiters, 1),
                source,
                distanceKm,
                Math.Round(diffLiters, 1),
                diffPercent
            ));
        }

        // Most suspect first (biggest positive litre gap).
        rows = rows.OrderByDescending(r => r.DiffLiters).ToList();

        return new FuelComparisonReportDto(
            startDate,
            endDate,
            Math.Round(rows.Sum(r => r.RealLiters), 1),
            Math.Round(rows.Sum(r => r.RealCost), 2),
            Math.Round(rows.Sum(r => r.GpsLiters), 1),
            rows.Count,
            rows.Count(r => r.GpsSource == "capteur"),
            rows
        );
    }

    private async Task<Dictionary<string, decimal>> GetCurrentFuelPricesAsync(int companyId, CancellationToken ct)
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
            .ToListAsync(ct);

        return prices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);
    }
}
