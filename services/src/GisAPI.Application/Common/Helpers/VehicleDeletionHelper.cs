using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Helpers;

/// <summary>
/// Deletes a vehicle together with every row that references it.
///
/// <para>The child tables (maintenance, costs, documents, stops, fuel, device
/// commands/events, accidents, trips, tows, reservations, …) hold foreign keys
/// to <c>vehicles.id</c> that were created with the default <c>RESTRICT</c>
/// rule, so a plain <c>DELETE</c> throws a Postgres 23503 FK violation the
/// moment the vehicle has any history — which is why deleting a vehicle
/// "silently failed".</para>
///
/// <para>Rather than hand-list the child tables (and risk forgetting one, or a
/// future one), the referencing tables are DISCOVERED from the Postgres catalog
/// and cleared first. Everything runs in a single transaction so a failure
/// leaves the vehicle and its data intact.</para>
/// </summary>
public static class VehicleDeletionHelper
{
    // Every FK that points at vehicles(id): "<is_nullable>|<table>.<column>".
    // The nullability decides the action (see below). Aliased to "Value" for
    // EF's scalar SqlQueryRaw mapping.
    private const string ReferencingColumnsSql = @"
        SELECT c.is_nullable || '|' || tc.table_name || '.' || kcu.column_name AS ""Value""
        FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
          ON tc.constraint_name = kcu.constraint_name
         AND tc.constraint_schema = kcu.constraint_schema
        JOIN information_schema.constraint_column_usage ccu
          ON tc.constraint_name = ccu.constraint_name
         AND tc.constraint_schema = ccu.constraint_schema
        JOIN information_schema.columns c
          ON c.table_schema = tc.constraint_schema
         AND c.table_name = tc.table_name
         AND c.column_name = kcu.column_name
        WHERE tc.constraint_type = 'FOREIGN KEY'
          AND ccu.table_name = 'vehicles'
          AND ccu.column_name = 'id'
          AND tc.table_name <> 'vehicles';";

    /// <summary>
    /// Cascade-delete the vehicle, atomically. For every table referencing
    /// vehicles(id):
    ///   • NOT NULL column  → the row is a true dependent (costs, maintenance,
    ///     trips, documents…) and is DELETED with the vehicle;
    ///   • NULLABLE column  → the row can live without a vehicle (a driver's
    ///     assigned_vehicle_id, a manual fuel entry, a gps alert…) and is
    ///     SET NULL so the record itself is preserved.
    /// The GPS device (if any) must be released by the caller BEFORE this runs —
    /// the device is unlinked, not deleted. No-op if the vehicle row is gone.
    /// </summary>
    public static async Task CascadeDeleteAsync(IGisDbContext context, int vehicleId, CancellationToken ct)
    {
        var db = context.Database;

        var rows = await db.SqlQueryRaw<string>(ReferencingColumnsSql).ToListAsync(ct);

        await using var tx = await db.BeginTransactionAsync(ct);

        foreach (var row in rows)
        {
            var bar = row.IndexOf('|');
            if (bar <= 0) continue;
            var nullable = row[..bar].Trim().Equals("YES", StringComparison.OrdinalIgnoreCase);
            var rest = row[(bar + 1)..];
            var dot = rest.LastIndexOf('.');
            if (dot <= 0) continue;
            var table = rest[..dot];
            var column = rest[(dot + 1)..];
            // Identifiers come from the catalog (not user input); the id is
            // parameterised. Quoted to preserve the exact catalog casing.
            var sql = nullable
                ? $"UPDATE \"{table}\" SET \"{column}\" = NULL WHERE \"{column}\" = {{0}}"
                : $"DELETE FROM \"{table}\" WHERE \"{column}\" = {{0}}";
            await db.ExecuteSqlRawAsync(sql, new object[] { vehicleId }, ct);
        }

        await db.ExecuteSqlRawAsync("DELETE FROM \"vehicles\" WHERE id = {0}", new object[] { vehicleId }, ct);

        await tx.CommitAsync(ct);
    }
}
