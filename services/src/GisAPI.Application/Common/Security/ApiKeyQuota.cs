using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Plafond d'utilisation d'une clé d'API (<see cref="ApiKeyAccess.MaxExchangesPer24Hours"/> sur
/// 24 heures glissantes), compté et consommé de façon ATOMIQUE.
///
/// <para>Revue de sécurité du 23/11/2026 : « compter puis insérer » laissait passer, au moment du
/// dépassement, autant d'appels qu'il en arrivait en même temps avec la même clé. La ligne de la clé
/// est donc verrouillée (<c>SELECT … FOR UPDATE</c>) le temps de compter ses utilisations et
/// d'enregistrer la nouvelle : deux appels simultanés passent l'un après l'autre et le second voit le
/// compte du premier. Le verrou ne porte que sur cette clé et dure quelques millisecondes.</para>
/// </summary>
public static class ApiKeyQuota
{
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private const string InMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    /// <summary>
    /// Enregistre <paramref name="usage"/> (jeton échangé ou appel direct) si la clé n'a pas atteint
    /// son plafond. Renvoie false, sans rien écrire, si le plafond est atteint.
    /// </summary>
    public static async Task<bool> TryConsumeAsync(
        IGisDbContext db, int apiKeyId, ApiToken usage, DateTime nowUtc, CancellationToken ct)
    {
        var database = db.Database;
        var since = nowUtc.AddHours(-24);

        async Task<bool> CountAndInsertAsync()
        {
            var used = await db.ApiTokens.IgnoreQueryFilters()
                .CountAsync(t => t.ApiKeyId == apiKeyId && t.CreatedAt > since, ct);
            if (used >= ApiKeyAccess.MaxExchangesPer24Hours)
                return false;
            db.ApiTokens.Add(usage);
            await db.SaveChangesAsync(ct);
            return true;
        }

        // Base en mémoire des tests : pas de transaction possible, pas de concurrence non plus.
        if (database.ProviderName == InMemoryProvider)
            return await CountAndInsertAsync();

        // Stratégie de l'API (EnableRetryOnFailure) : une erreur transitoire rejoue le tout.
        var strategy = database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await database.BeginTransactionAsync(ct);
            if (database.ProviderName == NpgsqlProvider)
                await database.ExecuteSqlRawAsync(
                    "SELECT id FROM api_keys WHERE id = {0} FOR UPDATE", new object[] { apiKeyId }, ct);

            var accepted = await CountAndInsertAsync();
            if (accepted)
                await tx.CommitAsync(ct);
            else
                await tx.RollbackAsync(ct);
            return accepted;
        });
    }
}
