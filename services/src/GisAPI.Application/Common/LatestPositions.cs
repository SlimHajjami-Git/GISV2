using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common;

/// <summary>
/// Dernière trame de chaque boîtier, en UNE lecture d'index par boîtier
/// (<c>(device_id, recorded_at DESC)</c>, LATERAL … LIMIT 1), quelle que soit la
/// profondeur de l'historique.
///
/// <para>Pourquoi un utilitaire (application mobile « horriblement lente », 22/11/2026) :
/// <c>GroupBy(p =&gt; p.DeviceId).Select(g =&gt; g.OrderByDescending(…).First())</c> est
/// traduit par EF en <c>… FROM gps_positions GROUP BY device_id</c> : PostgreSQL parcourt
/// TOUTES les trames de chaque boîtier avant de chercher la dernière. Mesuré sur TN
/// (gps_positions = 48 Go, 44 millions de lignes) : 12 s pour 5 boîtiers, plus de 180 s
/// pour les 86 de PARENIN, au-delà des 30 s où l'API abandonne. L'écran Carte, la liste
/// Véhicules et le tableau de bord de l'application mobile appelaient ce chemin
/// (<c>/gps/positions/latest</c>, <c>/dashboard/stats</c>) : marqueurs absents ou à leur
/// ancienne place jusqu'à la trame temps réel suivante. Forme LATERAL : 12 ms pour les
/// 359 boîtiers de HERTZ. La carte web l'utilisait déjà
/// (GetVehiclesWithPositionsQueryHandler).</para>
///
/// <para>La dernière trame se choisit sur <c>recorded_at</c>, jamais sur <c>id</c> : une
/// trame stockée par le boîtier et renvoyée plus tard reçoit un id plus grand qu'une
/// trame plus récente.</para>
/// </summary>
public static class LatestPositions
{
    public const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Dernière trame par boîtier (les boîtiers sans aucune trame sont absents).</summary>
    public static async Task<Dictionary<int, GpsPosition>> LoadAsync(
        IGisDbContext context, IEnumerable<int> deviceIds, CancellationToken ct)
    {
        var ids = deviceIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new Dictionary<int, GpsPosition>();

        if (context.Database.ProviderName == NpgsqlProvider)
        {
            var rows = await context.GpsPositions
                .FromSqlRaw(@"
SELECT p.*
FROM unnest({0}::integer[]) AS d(device_id)
CROSS JOIN LATERAL (
    SELECT * FROM gps_positions
    WHERE device_id = d.device_id
    ORDER BY recorded_at DESC
    LIMIT 1
) p", ids)
                .AsNoTracking()
                .ToListAsync(ct);
            return rows.ToDictionary(p => p.DeviceId);
        }

        // Autres fournisseurs (tests en mémoire / SQLite) : même résultat, une requête par boîtier.
        var result = new Dictionary<int, GpsPosition>();
        foreach (var id in ids)
        {
            var last = await context.GpsPositions.AsNoTracking()
                .Where(p => p.DeviceId == id)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync(ct);
            if (last != null)
                result[id] = last;
        }
        return result;
    }
}
