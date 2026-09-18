using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Helpers;

/// <summary>
/// Supprime un compte utilisateur en conservant l'historique qui le cite.
///
/// <para>Plusieurs clés étrangères vers <c>users.id</c> (12 sur TN, 18 en local) sont en NO ACTION
/// ou RESTRICT (audit_logs.UserId, vehicle_costs.created_by_user_id, reservations,
/// sinistres, messages…). audit_logs reçoit une ligne à CHAQUE connexion : un
/// <c>DELETE</c> simple levait 23503, donc une erreur 500, pour tout compte qui
/// s'était connecté une seule fois (constat du 16/09/2026).</para>
///
/// <para>Même convention que <see cref="VehicleDeletionHelper"/> : les colonnes
/// qui référencent le compte sont DÉCOUVERTES au catalogue (aucune liste codée en
/// dur, qui oublierait une table future) et traitées avant la suppression, dans
/// une seule transaction : un échec laisse le compte et ses données intacts.</para>
/// </summary>
public static class UserDeletionHelper
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string UsersTable = "users";

    // Seules tables dont les lignes obligatoires peuvent partir avec le compte : ses messages
    // et ses conversations avec l'assistant. Toute AUTRE colonne NOT NULL sans cascade est de
    // l'historique métier ; la nullabilité réelle de TN n'étant pas relevée, la supprimer
    // d'office effacerait des dépenses ou un journal au lieu de les détacher : refus en clair.
    private static readonly HashSet<string> ConversationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat_messages", "ai_chat_messages",
    };

    // Chaque clé étrangère vers users(id) : "<is_nullable>|<confdeltype>|<table>.<colonne>".
    // confdeltype : a = NO ACTION, r = RESTRICT, c = CASCADE, n = SET NULL, d = SET DEFAULT.
    // Aliasé en "Value" pour le SqlQueryRaw scalaire d'EF. Lu dans pg_catalog et non
    // information_schema, pour la même raison que VehicleDeletionHelper : 27,9 s sur le
    // catalogue de TN contre quelques millisecondes (mesuré le 09/09/2026).
    private const string ReferencingColumnsSql = @"
        SELECT CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END || '|' || c.confdeltype::text || '|' || cl.relname || '.' || a.attname AS ""Value""
        FROM pg_catalog.pg_constraint c
        JOIN pg_catalog.pg_class cl ON cl.oid = c.conrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = cl.relnamespace
        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
        WHERE c.contype = 'f'
          AND c.confrelid = 'public.users'::regclass
          AND n.nspname = 'public'
        ORDER BY cl.relname, a.attname;";

    // Même relevé pour SQLite (base des tests), qui n'a pas de pg_catalog : sans lui
    // les handlers ne pourraient pas être vérifiés avec les clés étrangères actives.
    private const string SqliteReferencingColumnsSql = @"
        SELECT CASE WHEN ti.""notnull"" = 1 THEN 'NO' ELSE 'YES' END || '|' ||
               CASE upper(fk.on_delete) WHEN 'CASCADE' THEN 'c' WHEN 'SET NULL' THEN 'n' WHEN 'SET DEFAULT' THEN 'd' WHEN 'RESTRICT' THEN 'r' ELSE 'a' END
               || '|' || m.name || '.' || fk.""from"" AS ""Value""
        FROM sqlite_master m
        JOIN pragma_foreign_key_list(m.name) fk
        JOIN pragma_table_info(m.name) ti ON ti.name = fk.""from""
        WHERE m.type = 'table' AND lower(fk.""table"") = 'users'
        ORDER BY m.name, fk.""from"";";

    /// <summary>
    /// Instructions ordonnées (identifiant du compte en {0}), une par colonne relevée
    /// au catalogue, puis la suppression du compte :
    ///   • colonne NULLABLE, quelle que soit sa règle (même CASCADE, comme
    ///     drivers.user_id) → mise à NULL : dépenses, trajets, journal d'audit,
    ///     sinistres, réservations et fiche chauffeur restent, détachés du compte ;
    ///   • NOT NULL en CASCADE → rien : la base supprime ce qui n'appartient qu'au
    ///     compte (notifications, jetons, affectations de véhicules, rapports programmés) ;
    ///   • NOT NULL en NO ACTION ou RESTRICT → suppression des lignes s'il s'agit de ses
    ///     messages ou de ses conversations avec l'assistant, qui ne peuvent exister sans lui ;
    ///     pour toute autre table, refus (DomainException) : rien n'est modifié ;
    ///   • NOT NULL en SET NULL ou SET DEFAULT → rien : la base applique sa propre règle
    ///     (ou refuse, et la transaction n'a rien modifié) plutôt que de perdre des lignes
    ///     qu'elle aurait gardées.
    /// </summary>
    public static List<string> BuildStatements(IEnumerable<string> referencingColumns)
    {
        var statements = new List<string>();

        foreach (var row in referencingColumns)
        {
            var parts = row.Split('|', 3);
            if (parts.Length != 3) continue;
            var nullable = parts[0].Trim().Equals("YES", StringComparison.OrdinalIgnoreCase);
            var rule = parts[1].Trim();
            var dot = parts[2].LastIndexOf('.');
            if (dot <= 0) continue;
            var table = parts[2][..dot];
            var column = parts[2][(dot + 1)..];

            if (nullable)
            {
                statements.Add($"UPDATE {Q(table)} SET {Q(column)} = NULL WHERE {Q(column)} = {{0}}");
                continue;
            }
            if (rule != "a" && rule != "r") continue;
            // Une référence obligatoire d'un compte vers un autre ne supprime jamais
            // d'autres comptes : la base refusera, sans rien modifier.
            if (table.Equals(UsersTable, StringComparison.OrdinalIgnoreCase)) continue;

            if (!ConversationTables.Contains(table))
                throw new DomainException(
                    $"Suppression impossible : des données ({table}) citent ce compte et ne peuvent pas en être détachées. Rien n'a été modifié.");

            statements.Add($"DELETE FROM {Q(table)} WHERE {Q(column)} = {{0}}");
        }

        statements.Add("DELETE FROM \"users\" WHERE id = {0}");
        return statements;
    }

    /// <summary>
    /// Supprime le compte atomiquement (voir <see cref="BuildStatements"/>). Les
    /// contrôles métier (société, soi-même…) restent à la charge de l'appelant ;
    /// sans effet si le compte n'existe plus. Un refus de la base devient un message
    /// en clair plutôt qu'une erreur 500 : rien n'a été modifié.
    /// </summary>
    public static async Task DeleteAsync(IGisDbContext context, int userId, CancellationToken ct)
    {
        var db = context.Database;

        try
        {
            var sql = db.ProviderName == NpgsqlProvider ? ReferencingColumnsSql : SqliteReferencingColumnsSql;
            var rows = await db.SqlQueryRaw<string>(sql).ToListAsync(ct);
            var statements = BuildStatements(rows);

            // Stratégie de l'API (EnableRetryOnFailure) : une erreur transitoire rejoue la
            // transaction entière, sans risque puisqu'elle ne fait que mettre à NULL et supprimer.
            var strategy = db.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.BeginTransactionAsync(ct);

                foreach (var statement in statements)
                    await db.ExecuteSqlRawAsync(statement, new object[] { userId }, ct);

                await tx.CommitAsync(ct);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not DomainException)
        {
            // Le texte technique de la base (anglais) reste dans l'exception interne, que
            // ExceptionHandlingMiddleware journalise : l'écran n'affiche que du français.
            throw new DomainException(
                "La base a refusé la suppression de l'utilisateur, rien n'a été modifié.", ex);
        }
    }

    // Identifiants lus au catalogue (jamais saisis) ; guillemets pour garder la casse exacte.
    private static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
