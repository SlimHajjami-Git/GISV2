using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
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

        // Portee vehicules : l'audit detaille les pleins factures (montant, station) d'un
        // vehicule precis. Fuite constatee : un employe restreint pouvait auditer
        // n'importe quel vehicule du parc en changeant l'identifiant dans l'URL.
        // scope == null => admin societe, aucun filtre supplementaire.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var vehicleQuery = _context.Vehicles
            .Include(v => v.GpsDevice)
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId);

        if (scope is not null)
            vehicleQuery = vehicleQuery.Where(v => scope.Contains(v.Id));

        var vehicle = await vehicleQuery.FirstOrDefaultAsync(ct);

        // Vehicule inconnu OU hors portee => rapport vide (aucune donnee carburant).
        if (vehicle == null)
        {
            return new FuelAuditReportDto(
                request.VehicleId, $"Vehicule {request.VehicleId}", null, false, 0,
                startDate, endDate,
                new List<FuelLevelPointDto>(), new List<CardFillDto>(),
                new List<DetectedRefillDto>(), new List<FillCheckDto>(), 0, 0, 0);
        }

        // GPS side: fuel-level curve + detected refills.
        var audit = await _fuelCalculationService.GetFuelLevelAuditAsync(vehicle, startDate, endDate, ct);

        // Conversion points→litres étalonnée sur les pleins facturés du véhicule
        // (12 derniers mois) — indépendante de la période du rapport. Sans assez
        // de points, conversion nominale avec fourchette large : le rapport ne
        // doit jamais afficher une précision que la jauge n'a pas.
        var calibration = await _fuelCalculationService.GetTankCalibrationAsync(vehicle, ct);

        decimal CalibratedLiters(GisAPI.Application.Features.FuelExpenses.DetectedRefillDto r) =>
            r.DeltaPoints > 0 ? calibration.ConvertToLiters(r.DeltaPoints) : r.Liters;

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
        var matchedRefuel = new bool[audit.DetectedRefills.Count];
        int confirmed = 0, notDetected = 0;

        foreach (var fill in fills)
        {
            int bestIdx = -1;
            double bestGap = double.MaxValue;
            for (int i = 0; i < audit.DetectedRefills.Count; i++)
            {
                var gap = Math.Abs((audit.DetectedRefills[i].T - fill.Date).TotalHours);
                if (gap <= windowHours && gap < bestGap)
                {
                    bestIdx = i;
                    bestGap = gap;
                }
            }

            if (bestIdx < 0)
            {
                notDetected++;
                checks.Add(new FillCheckDto(fill.Date, fill.Liters, null, null, null, "non_detecte"));
            }
            else
            {
                matchedRefuel[bestIdx] = true;
                var best = audit.DetectedRefills[bestIdx];
                var detected = CalibratedLiters(best);
                var (low, high) = best.DeltaPoints > 0
                    ? calibration.RangeFor(best.DeltaPoints)
                    : (detected, detected);
                string verdict;
                if (fill.Liters <= 0m)
                {
                    // Fill declared (amount only) but no volume entered; the GPS confirms a
                    // real refill, so it's a data-entry gap, not a volume mismatch/fraud.
                    verdict = "volume_non_saisi";
                }
                else
                {
                    // Cohérent si la facture tombe dans la fourchette de la jauge,
                    // élargie d'une marge commerciale : la jauge n'est pas un
                    // instrument de comptage, elle corrobore ou dément — c'est tout.
                    var tolerance = Math.Max(10m, Math.Max(fill.Liters * 0.25m, high - detected));
                    verdict = Math.Abs(detected - fill.Liters) <= tolerance ? "confirme" : "ecart";
                }
                if (verdict == "confirme") confirmed++;
                checks.Add(new FillCheckDto(
                    fill.Date, fill.Liters, best.T, detected, Math.Round(bestGap, 1), verdict,
                    low, high, best.DeltaPoints > 0 ? best.DeltaPoints : null));
            }
        }

        // Detected tank refills with NO declared card fill nearby = "rempli mais non
        // declare" (carburant entre dans le reservoir sans saisie dans la fiche reelle).
        // Seuil 15 points : en dessous, c'est du ballottement de gazole à bas niveau
        // (observé sur Scania 001 : oscillations quotidiennes 14→20 % à l'arrêt),
        // pas un plein — les lister noierait les vrais pleins manquants.
        const int minUndeclaredPoints = 15;
        int undeclared = 0;
        decimal undeclaredLiters = 0;
        for (int i = 0; i < audit.DetectedRefills.Count; i++)
        {
            if (matchedRefuel[i]) continue;
            var r = audit.DetectedRefills[i];
            if (r.DeltaPoints > 0 && r.DeltaPoints < minUndeclaredPoints) continue;
            undeclared++;
            var liters = CalibratedLiters(r);
            undeclaredLiters += liters;
            var (low, high) = r.DeltaPoints > 0 ? calibration.RangeFor(r.DeltaPoints) : (liters, liters);
            checks.Add(new FillCheckDto(null, 0m, r.T, liters, null, "non_declare",
                low, high, r.DeltaPoints > 0 ? r.DeltaPoints : null));
        }

        // Chronological order (by the declared fill date, else the detected refill moment).
        checks = checks
            .OrderBy(c => c.FillDate ?? c.MatchedRefillDate ?? DateTime.MaxValue)
            .ToList();

        // ── Synthèse — le chiffre que le gestionnaire cherche depuis toujours :
        // combien de gazole est ENTRÉ dans la cuve vs combien a été FACTURÉ. ──
        var totalBilled = fills.Sum(f => f.Liters);
        var totalDetected = audit.DetectedRefills
            .Where(r => r.DeltaPoints <= 0 || r.DeltaPoints >= minUndeclaredPoints)
            .Sum(CalibratedLiters);
        decimal? coverage = totalDetected > 0
            ? Math.Round(Math.Min(999, totalBilled / totalDetected * 100), 0)
            : null;

        // Prix moyen des factures de la période — pour traduire les litres
        // manquants en argent, la seule langue qui déclenche une réaction.
        var billedWithCost = fills.Where(f => f.Liters > 0 && f.Cost > 0).ToList();
        decimal? estimatedUndeclaredCost = billedWithCost.Count > 0 && undeclaredLiters > 0
            ? Math.Round(undeclaredLiters * (billedWithCost.Sum(f => f.Cost) / billedWithCost.Sum(f => f.Liters)), 0)
            : null;

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
            notDetected,
            undeclared,
            TotalBilledLiters: Math.Round(totalBilled, 1),
            TotalDetectedLiters: Math.Round(totalDetected, 1),
            UndeclaredLiters: Math.Round(undeclaredLiters, 1),
            CoveragePercent: coverage,
            EstimatedUndeclaredCost: estimatedUndeclaredCost,
            IsCalibrated: calibration.IsCalibrated,
            CalibrationPointCount: calibration.PointCount,
            EffectiveTankLiters: calibration.EffectiveTankLiters
        );
    }
}
