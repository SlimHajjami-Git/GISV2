using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicleBatteryHistory;

/// <summary>
/// Construit la courbe de tension batterie d'UN véhicule, à la demande.
///
/// <para><b>Pourquoi des tranches et pas les trames brutes.</b> Un boîtier émet
/// ~1 000 trames par jour : sept jours feraient 7 000 points, illisibles et lourds à
/// transporter. On agrège par tranches de largeur fixe, en gardant le MINIMUM et le
/// MAXIMUM de chaque tranche — surtout pas la moyenne, qui effacerait précisément les
/// chutes qu'on veut montrer. Relevé sur 262 TU 9816 (batterie en fin de vie), en
/// tranches de 2 h : 11,1 → 13,6 V en roulage, puis 7,0 → 7,7 V à l'arrêt le lendemain
/// matin. C'est cette dent de scie, et l'enfoncement de ses creux, qui rend la panne
/// évidente à l'œil.</para>
///
/// <para><b>Cloisonnement.</b> Même règle que partout : le véhicule doit appartenir à
/// la société du demandeur, et un utilisateur non-admin doit l'avoir en affectation.
/// Un identifiant d'une autre société rend <c>null</c>, donc 404 — jamais un indice
/// sur son existence.</para>
///
/// <para>Chemin FROID : appelé quand un admin ouvre la fenêtre depuis sa notification,
/// pas en polling. Il peut donc se permettre de lire les trames du véhicule.</para>
/// </summary>
public class GetVehicleBatteryHistoryQueryHandler
    : IRequestHandler<GetVehicleBatteryHistoryQuery, BatteryHistoryDto?>
{
    /// <summary>Bornes de la période demandée, pour qu'un paramètre d'URL ne puisse pas
    /// faire balayer des mois de trames.</summary>
    public const int MinDays = 1;
    public const int MaxDays = 30;
    public const int DefaultDays = 7;

    /// <summary>
    /// Nombre de tranches visé. ~250 points : assez pour que la dent de scie se voie,
    /// assez peu pour un tracé SVG fluide. Sur 7 jours cela fait des tranches de 40 min.
    /// </summary>
    private const int TargetBuckets = 250;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleBatteryHistoryQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<BatteryHistoryDto?> Handle(GetVehicleBatteryHistoryQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r =>
            r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct);

        if (vehicle == null) return null;

        if (!isAdmin && userId > 0)
        {
            var hasAccess = await _context.UserVehicles
                .AnyAsync(uv => uv.UserId == userId && uv.VehicleId == request.VehicleId, ct);
            if (!hasAccess) return null;
        }

        var days = Math.Clamp(request.Days <= 0 ? DefaultDays : request.Days, MinDays, MaxDays);
        var threshold = VoltageScale.NemsBatteryLowWarningV;
        var device = vehicle.GpsDevice;

        // Seuls les NEMS ont l'octet « Batterie ». Pour les autres, la courbe n'existe
        // pas : on le dit franchement plutôt que de rendre un graphe vide.
        if (device == null || !VoltageScale.IsNems(device.ProtocolType))
        {
            return new BatteryHistoryDto(vehicle.Id, vehicle.Plate, Supported: false, days,
                threshold, null, Array.Empty<BatteryHistoryPointDto>(), Array.Empty<BatteryStartPointDto>());
        }

        var since = DateTime.UtcNow.AddDays(-days);
        var bucketSeconds = Math.Max(60, (int)(TimeSpan.FromDays(days).TotalSeconds / TargetBuckets));

        // Agrégation par tranche, côté PostgreSQL : on ne ramène que ~250 lignes.
        // Le filtre de bande (68-92) écarte l'octet de cap des firmwares R00C30d, qui
        // dessinerait une fausse chute à 0 V.
        const string sql = @"
SELECT to_timestamp(floor(EXTRACT(epoch FROM recorded_at) / {1}) * {1}) AS ""AtUtc"",
       min(battery_raw)::int AS ""MinRaw"",
       max(battery_raw)::int AS ""MaxRaw""
FROM gps_positions
WHERE device_id = {0}
  AND recorded_at >= {2}
  AND battery_raw BETWEEN {3} AND {4}
GROUP BY 1
ORDER BY 1;
";
        var rows = await _context.Database
            .SqlQueryRaw<BucketRow>(sql, device.Id, bucketSeconds, since,
                VoltageScale.NemsBatteryMeaningfulMinRaw, VoltageScale.NemsBatteryMeaningfulMaxRaw)
            .ToListAsync(ct);

        var points = rows
            .Select(r => new BatteryHistoryPointDto(
                DateTime.SpecifyKind(r.AtUtc, DateTimeKind.Utc),
                VoltageScale.RoundVolts(r.MinRaw * VoltageScale.NemsBatteryFactor),
                VoltageScale.RoundVolts(r.MaxRaw * VoltageScale.NemsBatteryFactor)))
            .ToList();

        // Les démarrages : ce sont EUX que l'alerte juge. Les poser sur la courbe rend
        // le lien visible entre le graphe et la notification reçue.
        var starts = await _context.BatteryStartReadings
            .AsNoTracking()
            .Where(r => r.DeviceId == device.Id && r.StartAt >= since)
            .OrderBy(r => r.StartAt)
            .Select(r => new { r.StartAt, r.BatteryRaw })
            .ToListAsync(ct);

        var startPoints = starts
            .Select(s =>
            {
                var v = VoltageScale.RoundVolts(s.BatteryRaw * VoltageScale.NemsBatteryFactor);
                return new BatteryStartPointDto(s.StartAt, v, v < threshold);
            })
            .ToList();

        var medianV = VoltageScale.NemsMeaningfulVolts(device.BatteryStartMedianRaw);

        return new BatteryHistoryDto(
            vehicle.Id,
            vehicle.Plate,
            Supported: true,
            days,
            threshold,
            medianV == null ? null : VoltageScale.RoundVolts(medianV.Value),
            points,
            startPoints);
    }

    /// <summary>Porteur d'une tranche — Npgsql exige un type public concret.</summary>
    public class BucketRow
    {
        public DateTime AtUtc { get; set; }
        public int MinRaw { get; set; }
        public int MaxRaw { get; set; }
    }
}
