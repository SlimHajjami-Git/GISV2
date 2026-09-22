using System.Data;
using System.Data.Common;
using FluentAssertions;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GisAPI.Tests.Infrastructure;

/// <summary>
/// ai_usage_logs (migration 052) est une table écrite à la main, en snake_case : le mapping EF
/// doit reprendre EXACTEMENT les noms du SQL — sinon 42703/42P01 au premier appel à l'IA,
/// en production. Et la consommation du mois doit rester agrégée EN BASE par le fournisseur
/// de production (Npgsql), pas seulement par SQLite dans les autres tests.
/// </summary>
public class AiUsageLogMappingTests
{
    private const int CompanyId = 7;

    /// <summary>Aucune connexion réelle : la requête est traduite, notée, jamais exécutée.</summary>
    private sealed class SansConnexion : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    /// <summary>
    /// Note le SQL. Résultat simulé : 1 pour le comptage des scans (pour que leur somme soit
    /// aussi traduite), 0 pour une somme simple, aucune ligne pour un regroupement.
    /// </summary>
    private sealed class SqlNote : DbCommandInterceptor
    {
        public List<string> Commandes { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => InterceptionResult<DbDataReader>.SuppressWithResult(Noter(command));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(Noter(command)));

        private DbDataReader Noter(DbCommand command)
        {
            Commandes.Add(command.CommandText);
            var table = new DataTable();
            if (!command.CommandText.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase))
            {
                table.Columns.Add("valeur", typeof(int));
                table.Rows.Add(command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            }
            return table.CreateDataReader();
        }
    }

    private static GisDbContext Postgres(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql("Host=localhost;Database=traduction_seulement")
            .AddInterceptors(interceptors)
            .Options,
        TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    [Fact]
    public void Le_mapping_reprend_les_noms_snake_case_de_la_migration_052()
    {
        using var ctx = Postgres();
        var entite = ctx.Model.FindEntityType(typeof(AiUsageLog))!;

        entite.GetTableName().Should().Be("ai_usage_logs");
        var table = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table("ai_usage_logs", null);
        entite.GetProperties().ToDictionary(p => p.Name, p => p.GetColumnName(table)).Should().BeEquivalentTo(new Dictionary<string, string?>
        {
            ["Id"] = "id",
            ["CompanyId"] = "company_id",
            ["UserId"] = "user_id",
            ["Feature"] = "feature",
            ["TokensUsed"] = "tokens_used",
            ["CreatedAt"] = "created_at",
        });
        entite.FindProperty(nameof(AiUsageLog.Feature))!.GetMaxLength().Should().Be(32);
        entite.FindProperty(nameof(AiUsageLog.UserId))!.IsNullable.Should().BeTrue("un récit d'accident n'a pas d'utilisateur");
        entite.GetIndexes().Should().ContainSingle(i => i.GetDatabaseName() == "ix_ai_usage_logs_company_id_created_at");
    }

    [Fact]
    public async Task La_consommation_du_mois_est_agregee_en_base_par_PostgreSQL()
    {
        var sql = new SqlNote();
        await using var ctx = Postgres(new SansConnexion(), sql);

        await AiCredit.MonthlyConsumptionAsync(ctx, CompanyId, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);

        var usages = sql.Commandes.Should().ContainSingle(c => c.Contains("FROM ai_usage_logs")).Subject;
        usages.Should().Contain("GROUP BY", "une ligne par fonction remonte, pas une par appel")
            .And.Contain("sum(").And.Contain("count(")
            .And.Contain("company_id").And.Contain("created_at").And.Contain("feature");
        sql.Commandes.Where(c => c.Contains("FROM invoice_scan_logs")).Should().HaveCount(2, "comptage puis somme des scans")
            .And.Contain(c => c.Contains("sum("));
    }
}
