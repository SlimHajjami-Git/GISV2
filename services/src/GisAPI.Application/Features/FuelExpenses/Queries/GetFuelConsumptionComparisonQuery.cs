using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Consommation mesurée (jauge, ratchet étalonné) vs consommation RÉELLE
/// (méthode plein à plein : litres du plein de fin ÷ km odomètre) sur chaque
/// intervalle entre deux pleins facturés consécutifs. Une seule requête
/// positions pour toute la période, découpage en mémoire.
/// </summary>
public record GetFuelConsumptionComparisonQuery(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate) : IRequest<FuelConsumptionComparisonDto>;

public class GetFuelConsumptionComparisonQueryHandler
    : IRequestHandler<GetFuelConsumptionComparisonQuery, FuelConsumptionComparisonDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IFuelCalculationService _fuelCalculationService;

    public GetFuelConsumptionComparisonQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IFuelCalculationService fuelCalculationService)
    {
        _context = context;
        _tenantService = tenantService;
        _fuelCalculationService = fuelCalculationService;
    }

    public async Task<FuelConsumptionComparisonDto> Handle(GetFuelConsumptionComparisonQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var startDate = request.StartDate ?? DateTime.UtcNow.AddMonths(-1);
        var endDate = request.EndDate ?? DateTime.UtcNow;

        var empty = new FuelConsumptionComparisonDto(
            request.VehicleId, false, new List<ConsumptionComparisonIntervalDto>(), null, null, null);

        // Même portée que le reste du rapport.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        var vehicleQuery = _context.Vehicles
            .Include(v => v.GpsDevice)
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId);
        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));
        var vehicle = await vehicleQuery.FirstOrDefaultAsync(ct);
        if (vehicle == null)
            return empty;

        // Pleins facturés de la période (par id OU par plaque, comme le comparatif).
        var invoices = await _context.FuelEntries
            .AsNoTracking()
            .Where(fe => fe.CompanyId == companyId
                         && fe.Volume > 0
                         && fe.InvoiceDate >= startDate && fe.InvoiceDate <= endDate
                         && (fe.VehicleId == vehicle.Id
                             || (fe.VehicleId == null
                                 && !string.IsNullOrEmpty(vehicle.Plate)
                                 && fe.VehiclePlate == vehicle.Plate)))
            .OrderBy(fe => fe.InvoiceDate)
            .Select(fe => new { fe.InvoiceDate, fe.Volume })
            .ToListAsync(ct);

        if (invoices.Count < 2 || !vehicle.GpsDeviceId.HasValue)
            return empty with { HasSensor = vehicle.GpsDeviceId.HasValue };

        // UNE requête positions (trames avec jauge — sur NEMS L elles couvrent tout
        // le roulage et portent l'odomètre), découpée ensuite par intervalle.
        var framesFrom = invoices[0].InvoiceDate;
        var framesTo = invoices[invoices.Count - 1].InvoiceDate.AddDays(1);
        var frames = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value
                        && p.RecordedAt >= framesFrom
                        && p.RecordedAt <= framesTo
                        && p.FuelRaw != null)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.RecordedAt, Fuel = p.FuelRaw!.Value, p.OdometerKm, p.SpeedKph })
            .ToListAsync(ct);

        var hasSensor = frames.Count >= 3;

        var calibration = await _fuelCalculationService.GetTankCalibrationAsync(vehicle, ct);
        var litersPerPoint = calibration.LitersPerPoint;

        var vehicleType = vehicle.Type?.ToLower() ?? "berline";
        var maxReasonable = vehicleType switch
        {
            "camion" or "bus" => 65m,
            "camionnette" or "fourgon" or "utilitaire" or "minibus" => 33m,
            _ => 26m
        };

        var intervals = new List<ConsumptionComparisonIntervalDto>();
        int cursor = 0;

        for (int i = 1; i < invoices.Count; i++)
        {
            var from = invoices[i - 1].InvoiceDate;
            var to = invoices[i].InvoiceDate;
            // Facture datée au jour (minuit) : l'intervalle couvre jusqu'à la fin
            // de la journée du plein de fin, sinon on ampute le dernier jour.
            var toInclusive = to.TimeOfDay == TimeSpan.Zero ? to.AddDays(1) : to;

            // Avance du curseur (frames triées) jusqu'au début de l'intervalle.
            while (cursor < frames.Count && frames[cursor].RecordedAt < from) cursor++;
            int sliceStart = cursor;
            int sliceEnd = sliceStart;
            while (sliceEnd < frames.Count && frames[sliceEnd].RecordedAt < toInclusive) sliceEnd++;

            long minOdo = long.MaxValue, maxOdo = 0;
            decimal measuredLiters = 0m;
            bool sensorFault = false;
            int? lastFuel = null;

            for (int k = sliceStart; k < sliceEnd; k++)
            {
                var f = frames[k];
                if (f.OdometerKm is > 0)
                {
                    if (f.OdometerKm.Value < minOdo) minOdo = f.OdometerKm.Value;
                    if (f.OdometerKm.Value > maxOdo) maxOdo = f.OdometerKm.Value;
                }

                // Mêmes règles ratchet que l'analyse par tranches.
                if (lastFuel == null) { lastFuel = f.Fuel; continue; }
                var delta = f.Fuel - lastFuel.Value;
                if (delta >= 10)
                {
                    lastFuel = f.Fuel;                    // plein : pas de la consommation
                }
                else if (delta < 0)
                {
                    var drop = -delta;
                    if (drop < 50)
                    {
                        measuredLiters += drop * litersPerPoint;
                        if (drop >= 10 && (f.SpeedKph ?? 0) < 5) sensorFault = true;
                    }
                    else sensorFault = true;
                    lastFuel = f.Fuel;
                }
                // hausse < 10 pts : ballottement, référence inchangée
            }

            var km = (minOdo == long.MaxValue || maxOdo <= minOdo) ? 0m : maxOdo - minOdo;
            if (km < 10m)
                continue; // pas assez de roulage mesurable pour une comparaison honnête

            var realLPer100 = Math.Round(invoices[i].Volume / km * 100m, 2);
            decimal? measuredLPer100 = hasSensor && sliceEnd > sliceStart
                ? Math.Round(measuredLiters / km * 100m, 2) : null;

            var reliable = measuredLPer100.HasValue && !sensorFault
                           && measuredLPer100.Value >= 1m && measuredLPer100.Value <= maxReasonable;

            intervals.Add(new ConsumptionComparisonIntervalDto(
                from, to, Math.Round(km, 0),
                Math.Round(invoices[i].Volume, 1), realLPer100,
                measuredLPer100.HasValue ? Math.Round(measuredLiters, 1) : null,
                measuredLPer100, reliable));
        }

        // Moyennes pondérées par les km — le réel sur tous les intervalles,
        // le mesuré uniquement sur les fenêtres fiables.
        var totalKm = intervals.Sum(x => x.Km);
        var avgReal = totalKm > 0
            ? Math.Round(intervals.Sum(x => x.RealLiters) / totalKm * 100m, 2) : (decimal?)null;
        var reliableIntervals = intervals.Where(x => x.MeasuredReliable && x.MeasuredLiters.HasValue).ToList();
        var reliableKm = reliableIntervals.Sum(x => x.Km);
        var avgMeasured = reliableKm > 0
            ? Math.Round(reliableIntervals.Sum(x => x.MeasuredLiters!.Value) / reliableKm * 100m, 2) : (decimal?)null;
        var deltaPercent = (avgReal.HasValue && avgMeasured is > 0)
            ? Math.Round((avgReal.Value - avgMeasured.Value) / avgMeasured.Value * 100m, 1) : (decimal?)null;

        return new FuelConsumptionComparisonDto(
            vehicle.Id, hasSensor, intervals, avgReal, avgMeasured, deltaPercent);
    }
}
