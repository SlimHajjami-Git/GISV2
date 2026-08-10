using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Consommation par tranches de X km d'un véhicule, avec min/max et tonnage
/// déclaré par segment. SegmentKm borné à [10, 1000], défaut 100.
/// </summary>
public record GetConsumptionSegmentsQuery(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate,
    int SegmentKm = 100) : IRequest<ConsumptionSegmentsReportDto>;

public class GetConsumptionSegmentsQueryHandler
    : IRequestHandler<GetConsumptionSegmentsQuery, ConsumptionSegmentsReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetConsumptionSegmentsQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<ConsumptionSegmentsReportDto> Handle(GetConsumptionSegmentsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;
        var segmentKm = Math.Clamp(request.SegmentKm, 10, 1000);

        static ConsumptionSegmentsReportDto Empty(int vehicleId, int segKm) =>
            new(vehicleId, $"Vehicule {vehicleId}", segKm, false, 0, false,
                new List<ConsumptionSegmentDto>(),
                new ConsumptionSegmentsSummaryDto(0, 0, null, null, null, null, null, 0, 0));

        // Même règle de portée que l'audit carburant : un employé restreint ne
        // doit pas pouvoir analyser un véhicule hors de son périmètre via l'URL.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var vehicleQuery = _context.Vehicles
            .Include(v => v.GpsDevice)
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId);
        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));

        var vehicle = await vehicleQuery.FirstOrDefaultAsync(ct);
        if (vehicle == null)
            return Empty(request.VehicleId, segmentKm);

        var report = await _fuelCalculationService.GetConsumptionSegmentsAsync(
            vehicle, startDate, endDate, segmentKm, ct);
        if (report == null)
            return Empty(request.VehicleId, segmentKm);

        if (report.Segments.Count == 0)
            return report;

        // Enrichissement tonnage : chaque segment hérite de la période de
        // chargement couvrant son point médian.
        var loadPeriods = await _context.VehicleLoadPeriods
            .AsNoTracking()
            .Where(lp => lp.VehicleId == vehicle.Id &&
                         lp.StartTime <= endDate &&
                         (lp.EndTime == null || lp.EndTime >= startDate))
            .OrderBy(lp => lp.StartTime)
            .ToListAsync(ct);

        if (loadPeriods.Count == 0)
            return report;

        var enriched = report.Segments.Select(s =>
        {
            var mid = s.StartTime + (s.EndTime - s.StartTime) / 2;
            var period = loadPeriods.LastOrDefault(lp =>
                lp.StartTime <= mid && (lp.EndTime == null || lp.EndTime >= mid));
            return period == null ? s : s with { TonnageT = period.TonnageT };
        }).ToList();

        return report with { Segments = enriched };
    }
}
