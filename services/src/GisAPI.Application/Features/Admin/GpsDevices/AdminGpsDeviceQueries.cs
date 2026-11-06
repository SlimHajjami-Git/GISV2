using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.GpsDevices;

// ── Inventaire avant suppression ──

public record GetGpsDeviceDeletionImpactQuery(int DeviceId) : IRequest<GpsDeviceDeletionImpactDto?>;

public class GetGpsDeviceDeletionImpactQueryHandler : IRequestHandler<GetGpsDeviceDeletionImpactQuery, GpsDeviceDeletionImpactDto?>
{
    private readonly IGisDbContext _context;
    public GetGpsDeviceDeletionImpactQueryHandler(IGisDbContext context) => _context = context;

    public Task<GpsDeviceDeletionImpactDto?> Handle(GetGpsDeviceDeletionImpactQuery r, CancellationToken ct) =>
        GpsDeviceDeletionInventory.BuildAsync(_context, r.DeviceId, ct);
}

// ── Liste des boîtiers (toutes sociétés, avec ou sans véhicule) ──

public record AdminGpsDeviceDto(
    int DeviceId, string Imei, string? Mat, int CompanyId, string? CompanyName,
    int? VehicleId, string? Plate, string? VehicleName,
    string? ProtocolType, string? Brand, string? Model, string Status,
    DateTime? LastCommunication, DateTime CreatedAt);

/// <param name="CompanyId">Société, ou null pour toutes.</param>
/// <param name="UnassignedOnly">Seulement les fiches sans véhicule — celles qu'aucun écran ne montrait.</param>
/// <param name="Search">IMEI, MAT, plaque ou nom de véhicule (comparaison normalisée).</param>
public record GetAdminGpsDevicesQuery(int? CompanyId, bool UnassignedOnly, string? Search) : IRequest<List<AdminGpsDeviceDto>>;

public class GetAdminGpsDevicesQueryHandler : IRequestHandler<GetAdminGpsDevicesQuery, List<AdminGpsDeviceDto>>
{
    private const int MaxRows = 3000;
    private readonly IGisDbContext _context;
    public GetAdminGpsDevicesQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminGpsDeviceDto>> Handle(GetAdminGpsDevicesQuery r, CancellationToken ct)
    {
        var devices = _context.GpsDevices.AsNoTracking().IgnoreQueryFilters();
        if (r.CompanyId.HasValue) devices = devices.Where(d => d.CompanyId == r.CompanyId.Value);

        var rows = await (
            from d in devices
            join v0 in _context.Vehicles.AsNoTracking().IgnoreQueryFilters() on d.Id equals v0.GpsDeviceId into vj
            from v in vj.DefaultIfEmpty()
            join s0 in _context.Societes.AsNoTracking().IgnoreQueryFilters() on d.CompanyId equals s0.Id into sj
            from s in sj.DefaultIfEmpty()
            select new AdminGpsDeviceDto(
                d.Id, d.DeviceUid, d.Mat, d.CompanyId, s != null ? s.Name : null,
                v != null ? (int?)v.Id : null, v != null ? v.Plate : null, v != null ? v.Name : null,
                d.ProtocolType, d.Brand, d.Model, d.Status, d.LastCommunication, d.CreatedAt))
            .ToListAsync(ct);

        IEnumerable<AdminGpsDeviceDto> filtre = rows;
        if (r.UnassignedOnly) filtre = filtre.Where(x => x.VehicleId == null);

        var recherche = GpsDeviceUniquenessGuard.Normalize(r.Search);
        if (recherche.Length > 0)
            filtre = filtre.Where(x =>
                GpsDeviceUniquenessGuard.Normalize(x.Imei).Contains(recherche)
                || GpsDeviceUniquenessGuard.Normalize(x.Mat).Contains(recherche)
                || GpsDeviceUniquenessGuard.Normalize(x.Plate).Contains(recherche)
                || GpsDeviceUniquenessGuard.Normalize(x.VehicleName).Contains(recherche));

        return filtre
            .OrderByDescending(x => x.LastCommunication ?? DateTime.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .Take(MaxRows)
            .ToList();
    }
}

// ── Boîtiers libres d'une société : fiche véhicule de la console (point 4, 06/11/2026) ──

/// <summary>
/// Même forme que la liste de l'espace client (<c>GET /api/gps/devices/available</c>) : la fiche
/// véhicule recopie l'IMEI, le MAT, la SIM et le mode capteur de l'appareil choisi.
/// </summary>
public record AdminAvailableGpsDeviceDto(
    int Id, string DeviceUid, string? Label, string? SimNumber, string? SimOperator,
    string? Brand, string? Model, string Status, DateTime? LastCommunication,
    string? Mat, string FuelSensorMode);

/// <summary>
/// La fiche véhicule de la console appelait la liste de l'espace CLIENT, qui part avec la clé
/// client : refusée en session admin (liste toujours vide), ou, si une session client traînait
/// dans le navigateur, remplie avec le stock de la société de CE compte, quel que soit le
/// véhicule. Ici : la société DU VÉHICULE, la seule que l'enregistrement accepte pour un appareil
/// choisi (GpsDeviceResolver : « appartient à une autre société »). « Libre » comme côté
/// client : statut « unassigned » (ou vide) et aucun véhicule rattaché.
/// </summary>
public record GetAdminAvailableGpsDevicesQuery(int CompanyId) : IRequest<List<AdminAvailableGpsDeviceDto>>;

public class GetAdminAvailableGpsDevicesQueryHandler : IRequestHandler<GetAdminAvailableGpsDevicesQuery, List<AdminAvailableGpsDeviceDto>>
{
    private readonly IGisDbContext _context;
    public GetAdminAvailableGpsDevicesQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminAvailableGpsDeviceDto>> Handle(GetAdminAvailableGpsDevicesQuery r, CancellationToken ct)
    {
        var libres = await _context.GpsDevices.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.CompanyId == r.CompanyId
                        && (d.Status == "unassigned" || d.Status == null)
                        && !_context.Vehicles.Any(v => v.GpsDeviceId == d.Id))
            .OrderByDescending(d => d.LastCommunication)
            .Select(d => new AdminAvailableGpsDeviceDto(
                d.Id, d.DeviceUid, d.Label, d.SimNumber, d.SimOperator, d.Brand, d.Model,
                d.Status, d.LastCommunication, d.Mat, d.FuelSensorMode))
            .ToListAsync(ct);

        // Fiches « fantômes » (constat du 06/11/2026, 13 en production chez BELIVE) : l'ingestion
        // créait une fiche quand un boîtier NEMS collait à son IMEI l'écho d'une commande d'heure
        // (« 860141076681403at+cclk="26/10/25 »). Ce ne sont pas des boîtiers : jamais proposées.
        // Un vrai identifiant n'a que des chiffres et des lettres (IMEI, ou MAT pour certains modèles).
        return libres.Where(d => IdentifiantPropre(d.DeviceUid)).ToList();
    }

    private static bool IdentifiantPropre(string? uid) =>
        !string.IsNullOrEmpty(uid) && uid.All(char.IsAsciiLetterOrDigit);
}

// ── Liste de blocage ──

public record GpsDeviceBlocklistDto(
    string DeviceUid, DateTime BlockedAt, int? BlockedByUserId, int? DeviceIdBefore, string? Mat, int? CompanyIdBefore, string? Reason);

public record GetGpsDeviceBlocklistQuery : IRequest<List<GpsDeviceBlocklistDto>>;

public class GetGpsDeviceBlocklistQueryHandler : IRequestHandler<GetGpsDeviceBlocklistQuery, List<GpsDeviceBlocklistDto>>
{
    private readonly IGisDbContext _context;
    public GetGpsDeviceBlocklistQueryHandler(IGisDbContext context) => _context = context;

    public Task<List<GpsDeviceBlocklistDto>> Handle(GetGpsDeviceBlocklistQuery r, CancellationToken ct) =>
        _context.GpsDeviceBlocklist.AsNoTracking()
            .OrderByDescending(b => b.BlockedAt)
            .Select(b => new GpsDeviceBlocklistDto(b.DeviceUid, b.BlockedAt, b.BlockedByUserId, b.DeviceIdBefore, b.Mat, b.CompanyIdBefore, b.Reason))
            .ToListAsync(ct);
}

/// <summary>Réinstallation d'un boîtier supprimé : l'ingestion peut de nouveau créer sa fiche.</summary>
public record UnblockGpsDeviceCommand(string DeviceUid) : IRequest<bool>;

public class UnblockGpsDeviceCommandHandler : IRequestHandler<UnblockGpsDeviceCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public UnblockGpsDeviceCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<bool> Handle(UnblockGpsDeviceCommand r, CancellationToken ct)
    {
        var uid = (r.DeviceUid ?? string.Empty).Trim();
        var entry = await _context.GpsDeviceBlocklist.FirstOrDefaultAsync(b => b.DeviceUid == uid, ct);
        if (entry == null) return false;

        _context.GpsDeviceBlocklist.Remove(entry);
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _tenant.UserId,
            CompanyId = entry.CompanyIdBefore,
            Action = "gps_device_unblocked",
            EntityType = "gps_devices",
            EntityId = entry.DeviceIdBefore,
            EntityName = entry.DeviceUid,
            Description = $"Boîtier IMEI {entry.DeviceUid} retiré de la liste de blocage (bloqué le {entry.BlockedAt:dd/MM/yyyy HH:mm} UTC) : l'ingestion peut recréer sa fiche.",
            Timestamp = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
