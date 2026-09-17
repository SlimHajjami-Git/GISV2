using System.Globalization;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Documents;
using GisAPI.Application.Features.Repairs;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

public interface IVehicleHealthScoreService
{
    Task<VehicleHealthResult> CalculateScoreAsync(int vehicleId, int companyId, CancellationToken ct = default);
    Task<List<VehicleHealthResult>> CalculateAllScoresAsync(int companyId, CancellationToken ct = default);
}

public class VehicleHealthResult
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = "";
    public int Score { get; set; }
    public string Level { get; set; } = ""; // excellent, good, fair, poor, critical
    public List<HealthFactor> Factors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class HealthFactor
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public string Detail { get; set; } = "";
}

public class VehicleHealthScoreService : IVehicleHealthScoreService
{
    private readonly IServiceProvider _serviceProvider;

    public VehicleHealthScoreService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<VehicleHealthResult> CalculateScoreAsync(int vehicleId, int companyId, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();

        var vehicle = await context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId, ct);

        if (vehicle == null)
            return new VehicleHealthResult { VehicleId = vehicleId, Score = 0, Level = "unknown" };

        return await CalculateForVehicle(context, vehicle, ct);
    }

    /// <summary>
    /// Scores de TOUTE la flotte — 4 requêtes agrégées au lieu de 3 par véhicule.
    ///
    /// L'ancienne version bouclait sur chaque véhicule et exécutait 3 requêtes à
    /// l'intérieur (schedules, réparations, alertes) : ~1 + 219×3 = ~658 requêtes
    /// SÉQUENTIELLES chez HERTZ. C'était la cause PRINCIPALE des >80 s du
    /// dashboard (mesuré le 15/07). On agrège désormais les 3 compteurs par
    /// véhicule en 3 GROUP BY, puis on calcule les scores en mémoire.
    /// </summary>
    public async Task<List<VehicleHealthResult>> CalculateAllScoresAsync(int companyId, CancellationToken ct = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();

        var vehicles = await context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .ToListAsync(ct);
        if (vehicles.Count == 0) return new List<VehicleHealthResult>();

        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var now = DateTime.UtcNow;
        var sixMonthsAgo = now.AddMonths(-6);
        var thirtyDaysAgo = now.AddDays(-30);

        // 1 requête : entretiens par (véhicule, statut). Un échéancier en pause ou d'un
        // gabarit désactivé n'est plus recalculé : son statut reste figé (souvent « overdue »)
        // et retirait des points au score. Même périmètre que /vehicle-maintenance/stats.
        var schedRows = await context.VehicleMaintenanceSchedules
            .AsNoTracking()
            .Where(s => vehicleIds.Contains(s.VehicleId) && !s.IsPaused && s.Template!.IsActive)
            .GroupBy(s => new { s.VehicleId, s.Status })
            .Select(g => new { g.Key.VehicleId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var schedByVehicle = schedRows
            .GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Status ?? "", x => x.Count));

        // 1 requête : réparations des 6 derniers mois par véhicule. Une réparation annulée
        // n'a pas eu lieu : comptée, elle retirait des points « Réparations » et pouvait
        // classer le véhicule « à surveiller ». Casse et espaces ignorés (statuts anciens).
        var repairCounts = (await context.Repairs
            .AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId) && r.RepairDate >= sixMonthsAgo
                     && r.Status.Trim().ToLower() != RepairInputRules.Cancelled)
            .GroupBy(r => r.VehicleId)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x.Count);

        // 1 requête : alertes des 30 derniers jours par véhicule.
        var alertCounts = (await context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId != null && vehicleIds.Contains(a.VehicleId.Value) && a.Timestamp >= thirtyDaysAgo)
            .GroupBy(a => a.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.VehicleId, x => x.Count);

        var results = new List<VehicleHealthResult>(vehicles.Count);
        foreach (var vehicle in vehicles)
        {
            var statusCounts = schedByVehicle.GetValueOrDefault(vehicle.Id) ?? new Dictionary<string, int>();
            results.Add(ScoreVehicle(vehicle,
                statusCounts,
                repairCounts.GetValueOrDefault(vehicle.Id, 0),
                alertCounts.GetValueOrDefault(vehicle.Id, 0),
                now));
        }
        return results;
    }

    private async Task<VehicleHealthResult> CalculateForVehicle(IGisDbContext context, Vehicle vehicle, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var schedules = await context.VehicleMaintenanceSchedules
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicle.Id && !s.IsPaused && s.Template!.IsActive)
            .Select(s => s.Status)
            .ToListAsync(ct);
        var statusCounts = schedules
            .GroupBy(s => s ?? "")
            .ToDictionary(g => g.Key, g => g.Count());

        var sixMonthsAgo = now.AddMonths(-6);
        // Même périmètre que le calcul de flotte : réparations annulées exclues.
        var recentRepairs = await context.Repairs
            .AsNoTracking()
            .Where(r => r.VehicleId == vehicle.Id && r.RepairDate >= sixMonthsAgo
                     && r.Status.Trim().ToLower() != RepairInputRules.Cancelled)
            .CountAsync(ct);

        var thirtyDaysAgo = now.AddDays(-30);
        var alertCount = await context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId == vehicle.Id && a.Timestamp >= thirtyDaysAgo)
            .CountAsync(ct);

        return ScoreVehicle(vehicle, statusCounts, recentRepairs, alertCount, now);
    }

    /// <summary>
    /// Calcul PUR du score santé (aucun accès base) — partagé par le mode unitaire
    /// et le mode batch. <paramref name="statusCounts"/> = nombre d'entretiens par
    /// statut ; <paramref name="recentRepairs"/> = réparations sur 6 mois ;
    /// <paramref name="alertCount"/> = alertes sur 30 jours.
    /// </summary>
    private static VehicleHealthResult ScoreVehicle(
        Vehicle vehicle, Dictionary<string, int> statusCounts, int recentRepairs, int alertCount, DateTime now)
    {
        var result = new VehicleHealthResult
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name
        };

        var factors = new List<HealthFactor>();

        // ═══ FACTOR 1: Maintenance Schedule Compliance (25 pts) ═══
        var scheduleTotal = statusCounts.Values.Sum();
        int maintScore = 25;
        if (scheduleTotal > 0)
        {
            var overdue = statusCounts.GetValueOrDefault("overdue") + statusCounts.GetValueOrDefault("critical");
            var due = statusCounts.GetValueOrDefault("due");

            maintScore -= overdue * 8;
            maintScore -= due * 3;
            maintScore = Math.Max(0, maintScore);

            if (overdue > 0) result.Warnings.Add($"{overdue} entretien(s) en retard");
            if (due > 0) result.Warnings.Add($"{due} entretien(s) à effectuer bientôt");
        }
        factors.Add(new HealthFactor { Name = "Entretiens", Score = maintScore, MaxScore = 25, Detail = $"{statusCounts.GetValueOrDefault("ok")} à jour" });

        // ═══ FACTOR 2: Repair Frequency (20 pts) ═══
        int repairScore = 20;
        if (recentRepairs >= 5) { repairScore = 4; result.Warnings.Add($"{recentRepairs} réparations en 6 mois (fréquence élevée)"); }
        else if (recentRepairs >= 3) { repairScore = 10; }
        else if (recentRepairs >= 2) { repairScore = 15; }
        factors.Add(new HealthFactor { Name = "Réparations", Score = repairScore, MaxScore = 20, Detail = $"{recentRepairs} en 6 mois" });

        // ═══ FACTOR 3: Document Validity (15 pts) ═══
        var docScore = DocumentScore(vehicle, now, result.Warnings);
        factors.Add(new HealthFactor { Name = "Documents", Score = docScore, MaxScore = 15, Detail = docScore == 15 ? "Tous à jour" : "À vérifier" });

        // ═══ FACTOR 4: Alerts & Driving Behavior (20 pts) ═══
        int alertScore = 20;
        if (alertCount >= 20) { alertScore = 4; result.Warnings.Add($"{alertCount} alertes en 30 jours (comportement critique)"); }
        else if (alertCount >= 10) { alertScore = 10; }
        else if (alertCount >= 5) { alertScore = 15; }
        factors.Add(new HealthFactor { Name = "Alertes", Score = alertScore, MaxScore = 20, Detail = $"{alertCount} en 30 jours" });

        // ═══ FACTOR 5: Age & Mileage (20 pts) ═══
        int ageScore = 20;
        if (vehicle.Year.HasValue)
        {
            int age = now.Year - vehicle.Year.Value;
            if (age > 15) { ageScore -= 10; }
            else if (age > 10) { ageScore -= 6; }
            else if (age > 7) { ageScore -= 3; }

            if (vehicle.Mileage > 300000) { ageScore -= 8; result.Warnings.Add($"Kilométrage élevé: {vehicle.Mileage:N0} km"); }
            else if (vehicle.Mileage > 200000) { ageScore -= 5; }
            else if (vehicle.Mileage > 150000) { ageScore -= 2; }
        }
        ageScore = Math.Max(0, ageScore);
        factors.Add(new HealthFactor { Name = "Âge/Kilométrage", Score = ageScore, MaxScore = 20, Detail = $"{vehicle.Year ?? 0} · {vehicle.Mileage:N0} km" });

        // ═══ TOTAL ═══
        result.Factors = factors;
        result.Score = factors.Sum(f => f.Score);
        result.Level = result.Score switch
        {
            >= 85 => "excellent",
            >= 70 => "good",
            >= 50 => "fair",
            >= 30 => "poor",
            _ => "critical"
        };

        return result;
    }

    /// <summary>
    /// Points « Documents » (sur 15) et avertissements associés. Statut en jours
    /// calendaires UTC (<see cref="ExpiryCalendar"/>), comme l'écran Échéances : la
    /// comparaison d'instants retirait les points d'un document « expiré » dès le
    /// jour même de son échéance, et la date avertie pouvait tomber au lendemain.
    /// </summary>
    internal static int DocumentScore(Vehicle vehicle, DateTime now, List<string> warnings)
    {
        var today = now.Date;
        int docScore = 15;

        switch (ExpiryCalendar.Status(vehicle.InsuranceExpiry, today))
        {
            case ExpiryCalendar.Expired: docScore -= 5; warnings.Add("Assurance expirée"); break;
            case ExpiryCalendar.ExpiringSoon: docScore -= 2; warnings.Add($"Assurance expire le {ExpiryDay(vehicle.InsuranceExpiry!.Value)}"); break;
        }
        switch (ExpiryCalendar.Status(vehicle.TechnicalInspectionExpiry, today))
        {
            case ExpiryCalendar.Expired: docScore -= 5; warnings.Add("Contrôle technique expiré"); break;
            case ExpiryCalendar.ExpiringSoon: docScore -= 2; warnings.Add($"Contrôle technique expire le {ExpiryDay(vehicle.TechnicalInspectionExpiry!.Value)}"); break;
        }
        if (ExpiryCalendar.Status(vehicle.TaxExpiry, today) == ExpiryCalendar.Expired) { docScore -= 3; warnings.Add("Vignette expirée"); }

        return Math.Max(0, docScore);
    }

    private static string ExpiryDay(DateTime expiry) =>
        ExpiryCalendar.Day(expiry).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
