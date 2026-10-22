using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.Monitoring;

// ═══════════════════════════════════════════════════════════════════════════
// Connexions GPS en attente d'identification — rattachement CONFIRMÉ à la main.
//
// CONSTAT (EL MOUSSEM, 10/2026) : un NEMS_LITE R00C03 qui se reconnecte (coupure
// réseau, redémarrage du serveur) ne renvoie pas son AA00 ; ses positions n'ont
// ni IMEI ni MAT. L'ingestion les jetait jusqu'au redémarrage du boîtier par SMS :
// cinq véhicules perdus d'un coup au redémarrage du serveur du 21/10/2026.
//
// L'ingestion RETIENT maintenant ces trames. Ici, on propose le véhicule probable :
// parmi les boîtiers qui se sont tus juste avant, celui dont la dernière position
// connue est la plus proche de la première trame retenue (constaté du 19 au
// 21/10 : 0 à 13 m pour un véhicule garé, 110 à 250 m en roulant). Rien n'est
// rattaché sans le clic d'un administrateur : deux véhicules perdus ensemble et
// garés côte à côte au dépôt ne doivent jamais pouvoir être confondus.
// ═══════════════════════════════════════════════════════════════════════════

public record HeldConnectionSuggestionDto(
    int DeviceId,
    string Imei,
    string? Plate,
    string? CompanyName,
    DateTime? LastCommunication,
    DateTime LastPositionAt,
    int DistanceMeters,
    int GapMinutes);

public record HeldConnectionDto(
    string Peer,
    bool Open,
    DateTime FirstReceivedAt,
    DateTime LastReceivedAt,
    int Frames,
    long DroppedFrames,
    HeldPosition? FirstPosition,
    HeldPosition? LastPosition,
    List<HeldConnectionSuggestionDto> Suggestions,
    /// <summary>Plusieurs véhicules plausibles et proches : choisir avec soin.</summary>
    bool Ambiguous);

public record HeldConnectionsDto(bool IngestReachable, List<HeldConnectionDto> Connections);

/// <summary>Boîtier qui s'est tu avant la connexion en attente, avec sa dernière position.</summary>
public record HeldConnectionCandidate(
    int DeviceId,
    string Imei,
    string? Plate,
    string? CompanyName,
    DateTime? LastCommunication,
    DateTime LastPositionAt,
    double Lat,
    double Lon);

/// <summary>
/// Classement des véhicules candidats pour une connexion en attente. Pur, testé
/// avec les cas réels d'EL MOUSSEM.
/// </summary>
public static class HeldConnectionMatcher
{
    /// <summary>En dessous, toujours plausible (dérive GPS d'un véhicule garé, petit déplacement).</summary>
    public const double MinRadiusMeters = 500;
    /// <summary>Vitesse maximale admise entre la dernière position connue et la première trame retenue.</summary>
    public const double MaxSpeedKmh = 130;
    public const int MaxSuggestions = 3;

    public static (List<HeldConnectionSuggestionDto> Suggestions, bool Ambiguous) Rank(
        HeldPosition first, IEnumerable<HeldConnectionCandidate> candidates)
    {
        var plausibles = new List<HeldConnectionSuggestionDto>();
        foreach (var c in candidates)
        {
            var distance = DistanceMeters(first.Lat, first.Lon, c.Lat, c.Lon);
            var gapMinutes = Math.Max(0, (first.At - c.LastPositionAt).TotalMinutes);
            var reach = Math.Max(MinRadiusMeters, gapMinutes / 60.0 * MaxSpeedKmh * 1000);
            if (distance > reach)
                continue;
            plausibles.Add(new HeldConnectionSuggestionDto(
                c.DeviceId, c.Imei, c.Plate, c.CompanyName, c.LastCommunication, c.LastPositionAt,
                (int)Math.Round(distance), (int)Math.Round(gapMinutes)));
        }

        var ranked = plausibles.OrderBy(s => s.DistanceMeters).ThenBy(s => s.GapMinutes).Take(MaxSuggestions).ToList();
        // Ambigu : un second candidat presque aussi proche que le premier.
        var ambiguous = ranked.Count >= 2
            && ranked[1].DistanceMeters < Math.Max(300, 3 * ranked[0].DistanceMeters);
        return (ranked, ambiguous);
    }

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000;
        double ToRad(double d) => d * Math.PI / 180;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}

// ── Liste, avec le véhicule probable ────────────────────────────────────────

public record GetHeldConnectionsQuery() : ICommand<HeldConnectionsDto>;

public class GetHeldConnectionsQueryHandler : IRequestHandler<GetHeldConnectionsQuery, HeldConnectionsDto>
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly IGisDbContext _context;
    private readonly IGpsIngestConnexions _ingest;

    public GetHeldConnectionsQueryHandler(IGisDbContext context, IGpsIngestConnexions ingest)
    {
        _context = context;
        _ingest = ingest;
    }

    public async Task<HeldConnectionsDto> Handle(GetHeldConnectionsQuery request, CancellationToken ct)
    {
        var held = await _ingest.ListAsync(ct);
        if (held == null)
            return new HeldConnectionsDto(false, new List<HeldConnectionDto>());

        var result = new List<HeldConnectionDto>(held.Count);
        foreach (var c in held)
        {
            var suggestions = new List<HeldConnectionSuggestionDto>();
            var ambiguous = false;
            if (c.FirstPosition != null)
            {
                var candidates = await CandidatesAsync(c, c.FirstPosition, ct);
                (suggestions, ambiguous) = HeldConnectionMatcher.Rank(c.FirstPosition, candidates);
            }
            result.Add(new HeldConnectionDto(c.Peer, c.Open, c.FirstReceivedAt, c.LastReceivedAt, c.Frames,
                c.DroppedFrames, c.FirstPosition, c.LastPosition, suggestions, ambiguous));
        }
        return new HeldConnectionsDto(true, result);
    }

    /// <summary>
    /// Boîtiers du même protocole tus avant la connexion (2 min de marge) et
    /// depuis moins de 3 jours, dont la dernière position est à moins de ~55 km.
    /// Un boîtier en ligne a un last_communication postérieur : il n'est jamais
    /// candidat. SQL brut (dernière position par LATERAL), PostgreSQL seulement.
    /// </summary>
    private async Task<List<HeldConnectionCandidate>> CandidatesAsync(HeldConnection c, HeldPosition first, CancellationToken ct)
    {
        if (_context.Database.ProviderName != NpgsqlProvider)
            return new List<HeldConnectionCandidate>();

        // Heures passées en secondes Unix : aucune ambiguïté de fuseau
        // (Npgsql est en mode « legacy timestamp » dans cette API).
        var avant = new DateTimeOffset(DateTime.SpecifyKind(c.FirstReceivedAt, DateTimeKind.Utc)).ToUnixTimeSeconds() + 120;
        var depuis = avant - 3 * 24 * 3600;
        double latMin = first.Lat - 0.5, latMax = first.Lat + 0.5, lonMin = first.Lon - 0.6, lonMax = first.Lon + 0.6;

        var rows = await _context.Database.SqlQuery<CandidateRow>($@"
            SELECT d.id AS ""DeviceId"", d.device_uid AS ""Imei"", trim(v.plate_number) AS ""Plate"", s.name AS ""CompanyName"",
                   (d.last_communication AT TIME ZONE 'UTC') AS ""LastCommunication"",
                   (lp.recorded_at AT TIME ZONE 'UTC') AS ""LastPositionAt"",
                   lp.latitude AS ""Lat"", lp.longitude AS ""Lon""
            FROM gps_devices d
            JOIN vehicles v ON v.gps_device_id = d.id
            LEFT JOIN societes s ON s.id = v.company_id
            CROSS JOIN LATERAL (
                SELECT p.recorded_at, p.latitude, p.longitude FROM gps_positions p
                WHERE p.device_id = d.id ORDER BY p.recorded_at DESC LIMIT 1) lp
            WHERE d.protocol_type = {c.Protocol}
              AND d.last_communication < to_timestamp({avant})
              AND d.last_communication > to_timestamp({depuis})
              AND lp.latitude BETWEEN {latMin} AND {latMax}
              AND lp.longitude BETWEEN {lonMin} AND {lonMax}")
            .ToListAsync(ct);

        return rows.Select(r => new HeldConnectionCandidate(
            r.DeviceId, r.Imei, r.Plate, r.CompanyName,
            r.LastCommunication.HasValue ? DateTime.SpecifyKind(r.LastCommunication.Value, DateTimeKind.Utc) : null,
            DateTime.SpecifyKind(r.LastPositionAt, DateTimeKind.Utc), r.Lat, r.Lon)).ToList();
    }

    private sealed class CandidateRow
    {
        public int DeviceId { get; set; }
        public string Imei { get; set; } = "";
        public string? Plate { get; set; }
        public string? CompanyName { get; set; }
        public DateTime? LastCommunication { get; set; }
        public DateTime LastPositionAt { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
}

// ── Confirmation par un administrateur ──────────────────────────────────────

public record AttributeHeldConnectionCommand(string Peer, int DeviceId) : ICommand<AttributeHeldConnectionResult>;

public record AttributeHeldConnectionResult(bool Ok, string Message, string? Plate, int ReplayedFrames, bool ConnectionOpen);

public class AttributeHeldConnectionCommandHandler
    : IRequestHandler<AttributeHeldConnectionCommand, AttributeHeldConnectionResult>
{
    private readonly IGisDbContext _context;
    private readonly IGpsIngestConnexions _ingest;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<AttributeHeldConnectionCommandHandler> _logger;

    public AttributeHeldConnectionCommandHandler(
        IGisDbContext context,
        IGpsIngestConnexions ingest,
        ICurrentTenantService tenant,
        ILogger<AttributeHeldConnectionCommandHandler> logger)
    {
        _context = context;
        _ingest = ingest;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<AttributeHeldConnectionResult> Handle(AttributeHeldConnectionCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Peer))
            return new AttributeHeldConnectionResult(false, "Connexion non précisée.", null, 0, false);

        // IgnoreQueryFilters : l'écran admin n'a pas de tenant.
        var device = await _context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.Id == request.DeviceId, ct);
        if (device == null || string.IsNullOrWhiteSpace(device.DeviceUid))
            return new AttributeHeldConnectionResult(false, "Boîtier introuvable.", null, 0, false);

        var plate = device.Vehicle?.Plate;
        var by = _tenant.UserEmail ?? $"utilisateur {_tenant.UserId}";
        var result = await _ingest.AttributeAsync(request.Peer.Trim(), device.DeviceUid, by, ct);

        _logger.LogWarning(
            "Connexion GPS {Peer} rattachée par {By} au boîtier {DeviceId} ({Plate}) : {Outcome}, {Frames} trame(s)",
            request.Peer, by, device.Id, plate, result.Outcome, result.ReplayedFrames);

        return result.Outcome switch
        {
            HeldAttributionOutcome.Done => new AttributeHeldConnectionResult(true,
                result.ConnectionOpen
                    ? $"{plate ?? device.DeviceUid} : suivi repris, {result.ReplayedFrames} trame(s) récupérée(s)."
                    : $"{plate ?? device.DeviceUid} : {result.ReplayedFrames} trame(s) récupérée(s). La connexion était fermée ; si le boîtier est encore perdu, il apparaîtra sur une nouvelle ligne.",
                plate, result.ReplayedFrames, result.ConnectionOpen),
            HeldAttributionOutcome.UnknownConnection => new AttributeHeldConnectionResult(false,
                "Cette connexion n'est plus en attente (déjà rattachée, ou trop ancienne).", plate, 0, false),
            HeldAttributionOutcome.AlreadyIdentified => new AttributeHeldConnectionResult(false,
                $"Refusé : le boîtier s'est annoncé lui-même entre-temps, sous l'IMEI {result.Detail}.", plate, 0, false),
            _ => new AttributeHeldConnectionResult(false,
                $"L'ingestion GPS n'a pas pu rattacher la connexion ({result.Detail}).", plate, 0, false)
        };
    }
}
