using System.Globalization;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Common;

/// <summary>Dépenses d'un véhicule sur un mois (ou sur la période entière).</summary>
public sealed record CostBucket(decimal Fuel, decimal Maintenance, decimal Repair, decimal Other, int RepairCount)
{
    public static CostBucket Zero { get; } = new(0, 0, 0, 0, 0);

    public decimal Total => Fuel + Maintenance + Repair + Other;

    public CostBucket Plus(CostBucket other) => new(
        Fuel + other.Fuel,
        Maintenance + other.Maintenance,
        Repair + other.Repair,
        Other + other.Other,
        RepairCount + other.RepairCount);
}

/// <summary>Une réparation retenue (statut ≠ cancelled) dans la période.</summary>
public sealed record RepairRow(
    int Id,
    int VehicleId,
    DateTime Date,
    string? Description,
    string? RepairType,
    int? SupplierId,
    int? MileageAtRepair,
    decimal TotalCost,
    string Reference,
    string Status);

/// <summary>Dépenses et distance d'un véhicule sur la période demandée.</summary>
public sealed class VehicleCostData
{
    public required int VehicleId { get; init; }
    public required string VehicleName { get; init; }
    public string? Plate { get; init; }
    public string? DepartmentName { get; init; }

    /// <summary>Seaux mensuels — seuls les mois ayant au moins une écriture sont présents.</summary>
    public required IReadOnlyDictionary<(int Year, int Month), CostBucket> Months { get; init; }
    public required CostBucket Total { get; init; }

    /// <summary>Distance mesurée sur la période, null si aucune source exploitable.</summary>
    public decimal? DistanceKm { get; init; }
    /// <summary><see cref="OperatingCostAggregator.SourceOdometer"/> | <see cref="OperatingCostAggregator.SourceGps"/> | <see cref="OperatingCostAggregator.SourceNone"/>.</summary>
    public required string DistanceSource { get; init; }
    public bool ReliableDistance { get; init; }
    public int IgnoredOdometerReadings { get; init; }
    public int OdometerBreaks { get; init; }
    /// <summary>Km attribués à chaque mois (relevé aval pour le compteur, début de trajet pour le GPS).</summary>
    public required IReadOnlyDictionary<(int Year, int Month), decimal> MonthlyKm { get; init; }

    /// <summary>
    /// Véhicule « analysé » : au moins une dépense ou une distance. Une dépense
    /// négative isolée (remboursement d'assurance) compte comme activité, sinon
    /// la somme des lignes affichées ne retomberait plus sur le total.
    /// </summary>
    public bool HasActivity => Total.Total != 0 || DistanceKm is > 0;
}

/// <summary>Résultat du chargement : périmètre de véhicules, dépenses, réparations.</summary>
public sealed class OperatingCostData
{
    public required DateTime StartUtc { get; init; }
    public required DateTime EndExclusiveUtc { get; init; }
    public required IReadOnlyList<VehicleCostData> Vehicles { get; init; }
    public required IReadOnlyList<RepairRow> Repairs { get; init; }

    /// <summary>Tous les mois couverts par la période, dans l'ordre, mois vides compris.</summary>
    public IEnumerable<(int Year, int Month)> MonthsInRange()
    {
        var cur = new DateTime(StartUtc.Year, StartUtc.Month, 1);
        var lastDay = EndExclusiveUtc.AddDays(-1);
        var last = new DateTime(lastDay.Year, lastDay.Month, 1);
        while (cur <= last)
        {
            yield return (cur.Year, cur.Month);
            cur = cur.AddMonths(1);
        }
    }
}

/// <summary>
/// Charge UNE fois tout ce qu'il faut aux rapports de coûts (coût d'exploitation,
/// évolution mensuelle, classement, fréquence des réparations) et le restitue par
/// véhicule et par mois. Définitions métier (contrat du 04/09/2026) :
/// <list type="bullet">
///   <item>Carburant = <c>fuel_entries</c> (InvoiceDate, TotalAmount) + <c>vehicle_costs</c> type <c>fuel</c>.</item>
///   <item>Entretiens = <c>vehicle_costs</c> type <c>maintenance</c> ou <c>entretien</c>.</item>
///   <item>Réparations = <c>repairs</c> (SocieteId, RepairDate, TotalCost, statut ≠ cancelled).</item>
///   <item>Autres = <c>vehicle_costs</c> des autres types (même règle que le tableau de bord).
///     Les mensualités d'acquisition sont EXCLUES (pas des dépenses d'exploitation).</item>
///   <item>Distance : véhicule sans boîtier → relevés compteur des pleins
///     (<see cref="OdometerDistance"/>) ; véhicule équipé → trajets GPS terminés, avec
///     repli compteur si aucun trajet ; sinon aucune distance.</item>
/// </list>
/// <c>Repair</c> n'a PAS de filtre de requête global (clé <c>SocieteId</c>) : le filtre
/// société est explicite ici.
/// </summary>
public static class OperatingCostAggregator
{
    public const string SourceOdometer = "odometer";
    public const string SourceGps = "gps";
    public const string SourceNone = "none";

    private static readonly CultureInfo Fr = new("fr-FR");

    /// <summary>Borne basse inclusive : minuit UTC du jour de début (Npgsql exige Kind = Utc).</summary>
    public static DateTime StartUtc(DateTime startDate) =>
        DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);

    /// <summary>Borne haute EXCLUSIVE : minuit UTC du lendemain du jour de fin (le jour de fin est inclus).</summary>
    public static DateTime EndExclusiveUtc(DateTime endDate) =>
        DateTime.SpecifyKind(endDate.Date.AddDays(1), DateTimeKind.Utc);

    /// <summary>« Sept. 2025 » : fr-FR « MMM yyyy » avec la première lettre en majuscule.</summary>
    public static string MonthLabel(int year, int month)
    {
        var raw = new DateTime(year, month, 1).ToString("MMM yyyy", Fr);
        return raw.Length == 0 ? raw : char.ToUpper(raw[0], Fr) + raw[1..];
    }

    public static string VehicleDisplayName(string? name, string? brand, string? model)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        var fallback = $"{brand} {model}".Trim();
        return fallback;
    }

    public static async Task<OperatingCostData> LoadAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        DateTime startUtc,
        DateTime endExclusiveUtc,
        int? vehicleId,
        int? departmentId,
        CancellationToken ct)
    {
        var companyId = tenant.CompanyId ?? 0;

        // Portée : société + véhicules affectés à l'appelant (null = admin, tout le
        // parc ; liste vide = rien). Appliquée ICI, avant toute agrégation.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(context, tenant, ct);

        var vehiclesQuery = context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId);

        if (scope is not null)
            vehiclesQuery = vehiclesQuery.Where(v => scope.Contains(v.Id));
        if (departmentId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.DepartmentId == departmentId.Value);
        if (vehicleId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.Id == vehicleId.Value);

        var vehicles = await vehiclesQuery
            .OrderBy(v => v.Id)
            .Select(v => new
            {
                v.Id,
                v.Name,
                v.Brand,
                v.Model,
                v.Plate,
                v.GpsDeviceId,
                DepartmentName = v.Department != null ? v.Department.Name : null
            })
            .ToListAsync(ct);

        if (vehicles.Count == 0)
        {
            return new OperatingCostData
            {
                StartUtc = startUtc,
                EndExclusiveUtc = endExclusiveUtc,
                Vehicles = Array.Empty<VehicleCostData>(),
                Repairs = Array.Empty<RepairRow>()
            };
        }

        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        // ── Carburant (pleins saisis) ─────────────────────────────────────────
        var fuelEntries = await context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startUtc
                     && f.InvoiceDate < endExclusiveUtc)
            .Select(f => new { VehicleId = f.VehicleId!.Value, f.TotalAmount, f.OdometerKm, f.InvoiceDate })
            .ToListAsync(ct);

        // ── Dépenses (carburant / entretien / autres, ventilées en C#) ────────
        var costs = await context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && vehicleIds.Contains(c.VehicleId)
                     && c.Date >= startUtc
                     && c.Date < endExclusiveUtc)
            .Select(c => new { c.VehicleId, c.Type, c.Amount, c.Date })
            .ToListAsync(ct);

        // ── Réparations ───────────────────────────────────────────────────────
        var repairRows = await context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId
                     && vehicleIds.Contains(r.VehicleId)
                     && r.RepairDate >= startUtc
                     && r.RepairDate < endExclusiveUtc)
            .Select(r => new RepairRow(
                r.Id, r.VehicleId, r.RepairDate, r.Description, r.RepairType, r.SupplierId,
                r.MileageAtRepair, r.TotalCost, r.Reference, r.Status))
            .ToListAsync(ct);

        var repairs = repairRows
            .Where(r => !string.Equals(r.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToList();

        // ── Trajets GPS (véhicules équipés seulement) ─────────────────────────
        var gpsVehicleIds = vehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.Id).ToList();
        var trips = gpsVehicleIds.Count == 0
            ? new List<(int VehicleId, DateTime StartTime, decimal DistanceKm)>()
            : (await context.Trips.AsNoTracking()
                .Where(t => t.CompanyId == companyId
                         && gpsVehicleIds.Contains(t.VehicleId)
                         && t.Status == "completed"
                         && t.StartTime >= startUtc
                         && t.StartTime < endExclusiveUtc)
                .Select(t => new { t.VehicleId, t.StartTime, t.DistanceKm })
                .ToListAsync(ct))
              .Select(t => (t.VehicleId, t.StartTime, t.DistanceKm))
              .ToList();

        // ── Agrégation par véhicule / mois ────────────────────────────────────
        var fuelByVehicle = fuelEntries.ToLookup(f => f.VehicleId);
        var costsByVehicle = costs.ToLookup(c => c.VehicleId);
        var repairsByVehicle = repairs.ToLookup(r => r.VehicleId);
        var tripsByVehicle = trips.ToLookup(t => t.VehicleId);

        var result = new List<VehicleCostData>(vehicles.Count);
        foreach (var v in vehicles)
        {
            var months = new Dictionary<(int Year, int Month), Accumulator>();
            Accumulator Bucket(DateTime d)
            {
                var k = (d.Year, d.Month);
                if (!months.TryGetValue(k, out var acc)) { acc = new Accumulator(); months[k] = acc; }
                return acc;
            }

            foreach (var f in fuelByVehicle[v.Id])
                Bucket(f.InvoiceDate).Fuel += f.TotalAmount;

            foreach (var c in costsByVehicle[v.Id])
            {
                var type = (c.Type ?? string.Empty).Trim().ToLowerInvariant();
                var acc = Bucket(c.Date);
                if (type == "fuel") acc.Fuel += c.Amount;
                else if (type == "maintenance" || type == "entretien") acc.Maintenance += c.Amount;
                else acc.Other += c.Amount;
            }

            foreach (var r in repairsByVehicle[v.Id])
            {
                var acc = Bucket(r.Date);
                acc.Repair += r.TotalCost;
                acc.RepairCount++;
            }

            var frozen = months.ToDictionary(kv => kv.Key, kv => kv.Value.Freeze());
            var total = frozen.Values.Aggregate(CostBucket.Zero, (a, b) => a.Plus(b));

            // ── Distance ──
            var odo = OdometerDistance.Compute(fuelByVehicle[v.Id].Select(f => (f.OdometerKm ?? 0L, f.InvoiceDate)));
            var hasGps = v.GpsDeviceId.HasValue;
            var vehicleTrips = tripsByVehicle[v.Id].ToList();
            var tripKm = vehicleTrips.Sum(t => t.DistanceKm);

            decimal? distance;
            string source;
            bool reliable;
            IReadOnlyDictionary<(int Year, int Month), decimal> monthlyKm;

            if (hasGps && tripKm > 0)
            {
                distance = tripKm;
                source = SourceGps;
                reliable = true;
                monthlyKm = vehicleTrips
                    .GroupBy(t => (t.StartTime.Year, t.StartTime.Month))
                    .ToDictionary(g => g.Key, g => g.Sum(t => t.DistanceKm));
            }
            else if (odo.Measurable)
            {
                distance = odo.DistanceKm;
                source = SourceOdometer;
                reliable = odo.Reliable;
                monthlyKm = odo.MonthlyKm;
            }
            else
            {
                distance = null;
                source = SourceNone;
                reliable = false;
                monthlyKm = new Dictionary<(int Year, int Month), decimal>();
            }

            result.Add(new VehicleCostData
            {
                VehicleId = v.Id,
                VehicleName = VehicleDisplayName(v.Name, v.Brand, v.Model),
                Plate = v.Plate,
                DepartmentName = v.DepartmentName,
                Months = frozen,
                Total = total,
                DistanceKm = distance,
                DistanceSource = source,
                ReliableDistance = reliable,
                IgnoredOdometerReadings = odo.IgnoredReadings,
                OdometerBreaks = odo.Breaks,
                MonthlyKm = monthlyKm
            });
        }

        return new OperatingCostData
        {
            StartUtc = startUtc,
            EndExclusiveUtc = endExclusiveUtc,
            Vehicles = result,
            Repairs = repairs
        };
    }

    private sealed class Accumulator
    {
        public decimal Fuel;
        public decimal Maintenance;
        public decimal Repair;
        public decimal Other;
        public int RepairCount;

        public CostBucket Freeze() => new(Fuel, Maintenance, Repair, Other, RepairCount);
    }
}
