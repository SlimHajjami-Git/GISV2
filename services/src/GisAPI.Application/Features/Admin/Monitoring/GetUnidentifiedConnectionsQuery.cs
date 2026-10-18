using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Monitoring;

/// <summary>
/// Connexions GPS « non identifiées » des dernières 24 h, pour la page System Health.
///
/// CONSTAT (EL MOUSSEM, 10/2026) : les NEMS_LITE sur SIM Ooredoo n'envoient leur
/// identité (AA00/AA01) qu'au démarrage, et leurs trames de position n'ont pas de
/// préfixe MAT. À chaque reconnexion, l'ingestion jette donc TOUT sans rien écrire
/// en base : six véhicules sont restés muets des jours sans qu'aucun écran ne le
/// montre. L'ingestion trace désormais ces rejets dans frame_debug_log (reason
/// « connexion_non_identifiee… », throttlé par IP source) — cette requête les
/// remonte pour qu'un boîtier muet se voie TOUT DE SUITE, avec la consigne
/// (redémarrage SMS) à côté.
///
/// Le motif du reason est un CONTRAT avec transport.rs (gps-ingest-rust) : ne pas
/// le renommer d'un seul côté.
/// </summary>
public record GetUnidentifiedConnectionsQuery() : ICommand<UnidentifiedConnectionsDto>;

public record UnidentifiedConnectionTraceDto(
    DateTime At,
    string? Peer,
    string? Mat,
    string FrameType,
    string Reason,
    string RawFrameExtract
);

public record UnidentifiedConnectionsDto(
    int Traces24h,
    DateTime? LastAt,
    List<UnidentifiedConnectionTraceDto> Latest
);

public class GetUnidentifiedConnectionsQueryHandler
    : IRequestHandler<GetUnidentifiedConnectionsQuery, UnidentifiedConnectionsDto>
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly IGisDbContext _context;

    public GetUnidentifiedConnectionsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<UnidentifiedConnectionsDto> Handle(GetUnidentifiedConnectionsQuery request, CancellationToken ct)
    {
        // frame_debug_log n'est pas mappée par EF (table technique de l'ingestion
        // Rust) : SQL brut, et rien hors PostgreSQL (tests SQLite → vide).
        if (_context.Database.ProviderName != NpgsqlProvider)
            return new UnidentifiedConnectionsDto(0, null, new List<UnidentifiedConnectionTraceDto>());

        var total = (await _context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM frame_debug_log " +
                "WHERE reason LIKE 'connexion_non_identifiee%' AND created_at >= now() - interval '24 hours'")
            .ToListAsync(ct)).FirstOrDefault();

        var rows = await _context.Database
            .SqlQueryRaw<TraceRow>(
                "SELECT created_at AS \"At\", peer AS \"Peer\", mat AS \"Mat\", " +
                "       frame_type AS \"FrameType\", reason AS \"Reason\", " +
                "       left(raw_frame, 120) AS \"RawFrameExtract\" " +
                "FROM frame_debug_log " +
                "WHERE reason LIKE 'connexion_non_identifiee%' AND created_at >= now() - interval '24 hours' " +
                "ORDER BY created_at DESC LIMIT 50")
            .ToListAsync(ct);

        return new UnidentifiedConnectionsDto(
            total,
            rows.Count > 0 ? rows[0].At : null,
            rows.Select(r => new UnidentifiedConnectionTraceDto(
                r.At, r.Peer, r.Mat, r.FrameType ?? "????", r.Reason ?? "", r.RawFrameExtract ?? "")).ToList()
        );
    }

    private sealed class TraceRow
    {
        public DateTime At { get; set; }
        public string? Peer { get; set; }
        public string? Mat { get; set; }
        public string? FrameType { get; set; }
        public string? Reason { get; set; }
        public string? RawFrameExtract { get; set; }
    }
}
