using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

public class GetFuelAuditReportQueryHandler
    : IRequestHandler<GetFuelAuditReportQuery, FuelAuditReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetFuelAuditReportQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<FuelAuditReportDto> Handle(GetFuelAuditReportQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;

        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct);

        if (vehicle == null)
        {
            return new FuelAuditReportDto(
                request.VehicleId, $"Vehicule {request.VehicleId}", null, false, 0,
                startDate, endDate,
                new List<FuelLevelPointDto>(), new List<CardFillDto>(),
                new List<DetectedRefillDto>(), new List<FillCheckDto>(), 0, 0);
        }

        // GPS side: fuel-level curve + detected refills.
        var audit = await _fuelCalculationService.GetFuelLevelAuditAsync(vehicle, startDate, endDate, ct);

        // Real side: billed card fills for this vehicle (by id, or by plate for unlinked entries).
        var fills = await _context.FuelEntries
            .Where(fe => fe.CompanyId == companyId
                         && fe.InvoiceDate >= startDate && fe.InvoiceDate <= endDate
                         && (fe.VehicleId == request.VehicleId
                             || (fe.VehicleId == null
                                 && !string.IsNullOrEmpty(vehicle.Plate)
                                 && fe.VehiclePlate == vehicle.Plate)))
            .OrderBy(fe => fe.InvoiceDate)
            .Select(fe => new CardFillDto(fe.InvoiceDate, fe.Volume, fe.TotalAmount, fe.StationName))
            .ToListAsync(ct);

        // Match each fill to the nearest detected refill within +/- 48h.
        const double windowHours = 48.0;
        var checks = new List<FillCheckDto>();
        int confirmed = 0, notDetected = 0;

        foreach (var fill in fills)
        {
            DetectedRefillDto? best = null;
            double bestGap = double.MaxValue;
            foreach (var r in audit.DetectedRefills)
            {
                var gap = Math.Abs((r.T - fill.Date).TotalHours);
                if (gap <= windowHours && gap < bestGap)
                {
                    best = r;
                    bestGap = gap;
                }
            }

            if (best == null)
            {
                notDetected++;
                checks.Add(new FillCheckDto(fill.Date, fill.Liters, null, null, null, "non_detecte"));
            }
            else
            {
                var tolerance = Math.Max(10m, fill.Liters * 0.4m);
                var verdict = Math.Abs(best.Liters - fill.Liters) <= tolerance ? "confirme" : "ecart";
                if (verdict == "confirme") confirmed++;
                checks.Add(new FillCheckDto(
                    fill.Date, fill.Liters, best.T, best.Liters, Math.Round(bestGap, 1), verdict));
            }
        }

        return new FuelAuditReportDto(
            vehicle.Id,
            string.IsNullOrWhiteSpace(vehicle.Name) ? (vehicle.Plate ?? $"Vehicule {vehicle.Id}") : vehicle.Name,
            vehicle.Plate,
            audit.HasSensor,
            audit.TankCapacity,
            startDate,
            endDate,
            audit.LevelSeries,
            fills,
            audit.DetectedRefills,
            checks,
            confirmed,
            notDetected
        );
    }
}
