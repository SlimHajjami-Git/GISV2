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

        // ── Ancres temporelles : l'INSTANT réel de chaque plein, lu sur la jauge ──
        // Les factures sont datées au JOUR (minuit) : borner les intervalles sur
        // les dates de facture comptait dans un intervalle les km roulés APRÈS
        // le plein de fin (avec le carburant du plein suivant !) et doublait les
        // journées de plein entre intervalles adjacents → km gonflés de ~30-50 %,
        // consommation « réelle » irréaliste (27 L/100 sur un camion à 43).
        // On se cale sur la remontée de jauge ≥ 10 pts la plus proche (±36 h) ;
        // à défaut, midi du jour de facture.
        var refillTimes = new List<DateTime>();
        {
            int? lf = null;
            foreach (var f in frames)
            {
                if (lf == null) { lf = f.Fuel; continue; }
                var d = f.Fuel - lf.Value;
                if (d >= 10)
                {
                    if (refillTimes.Count == 0 || (f.RecordedAt - refillTimes[refillTimes.Count - 1]).TotalMinutes > 30)
                        refillTimes.Add(f.RecordedAt);
                    lf = f.Fuel;
                }
                else if (d < 0) lf = f.Fuel;
            }
        }

        DateTime AnchorFor(DateTime invoiceDate)
        {
            var reference = invoiceDate.TimeOfDay == TimeSpan.Zero ? invoiceDate.AddHours(12) : invoiceDate;
            var best = reference;
            var bestHours = 36.0;
            foreach (var t in refillTimes)
            {
                var h = Math.Abs((t - reference).TotalHours);
                if (h < bestHours) { bestHours = h; best = t; }
            }
            return best;
        }

        int LowerBound(DateTime t)
        {
            int lo = 0, hi = frames.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (frames[mid].RecordedAt < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        var anchors = invoices.Select(iv => AnchorFor(iv.InvoiceDate)).ToList();

        var intervals = new List<ConsumptionComparisonIntervalDto>();

        for (int i = 1; i < invoices.Count; i++)
        {
            var from = anchors[i - 1];
            var to = anchors[i];
            if (to <= from) continue; // deux factures ancrées sur le même plein

            int sliceStart = LowerBound(from);
            int sliceEnd = LowerBound(to); // strict : exclut la trame du saut de fin

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

            // Raison précise quand la mesure est écartée — affichée au survol
            // et transmise à l'IA (un sec « non exploitable » n'explique rien).
            string? issue = null;
            if (!measuredLPer100.HasValue) issue = "pas de données de jauge sur la fenêtre";
            else if (sensorFault) issue = "chute de jauge anormale sur la fenêtre (incident capteur)";
            else if (measuredLPer100.Value < 1m) issue = "jauge figée sur la fenêtre (aucune baisse mesurée)";
            else if (measuredLPer100.Value > maxReasonable) issue = "valeur invraisemblable pour ce type de véhicule";
            // CHOIX PRODUIT (Slim, 12/08) : pas de grisage — toutes les fenêtres
            // mesurées comptent comme vraies (barres normales + incluses dans
            // les moyennes). Le diagnostic reste calculé dans MeasuredIssue à
            // titre informatif (API). Pour réactiver le garde-fou visuel :
            // var reliable = issue == null;
            var reliable = measuredLPer100.HasValue;

            intervals.Add(new ConsumptionComparisonIntervalDto(
                from, to, Math.Round(km, 0),
                Math.Round(invoices[i].Volume, 1), realLPer100,
                measuredLPer100.HasValue ? Math.Round(measuredLiters, 1) : null,
                measuredLPer100, reliable, issue));
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
