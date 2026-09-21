using GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GisAPI.Infrastructure.Persistence;

/// <summary>
/// Implémentation PostgreSQL de <see cref="ICompanyDataStore"/> : le catalogue vient de
/// information_schema / pg_constraint, les ordres passent par EF (paramétrés) dans une
/// transaction du DbContext courant.
/// </summary>
public class CompanyDataStore : ICompanyDataStore
{
    private readonly GisDbContext _context;
    private IDbContextTransaction? _tx;

    public CompanyDataStore(GisDbContext context) => _context = context;

    private sealed class ColumnRow { public string table_name { get; set; } = ""; public string column_name { get; set; } = ""; }
    private sealed class FkRow
    {
        public string child { get; set; } = ""; public string child_col { get; set; } = "";
        public string parent { get; set; } = ""; public string parent_col { get; set; } = "";
        public string del_rule { get; set; } = "a";
    }

    public async Task<CompanyDataCatalog> LoadCatalogAsync(CancellationToken ct)
    {
        var columns = await _context.Database.SqlQueryRaw<ColumnRow>(
            "SELECT c.table_name, c.column_name FROM information_schema.columns c " +
            "JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name " +
            "WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' ORDER BY c.table_name, c.ordinal_position")
            .ToListAsync(ct);

        var fks = await _context.Database.SqlQueryRaw<FkRow>(
            "SELECT ch.relname AS child, ca.attname AS child_col, pr.relname AS parent, pa.attname AS parent_col, k.confdeltype::text AS del_rule " +
            "FROM pg_constraint k " +
            "JOIN pg_class ch ON ch.oid = k.conrelid " +
            "JOIN pg_class pr ON pr.oid = k.confrelid " +
            "JOIN pg_namespace ns ON ns.oid = ch.relnamespace AND ns.nspname = 'public' " +
            "JOIN pg_attribute ca ON ca.attrelid = k.conrelid AND ca.attnum = k.conkey[1] " +
            "JOIN pg_attribute pa ON pa.attrelid = k.confrelid AND pa.attnum = k.confkey[1] " +
            "WHERE k.contype = 'f' AND array_length(k.conkey, 1) = 1 ORDER BY 1, 2")
            .ToListAsync(ct);

        var tables = columns.GroupBy(c => c.table_name)
            .Select(g => new CatalogTable(
                g.Key,
                g.Where(c => CompanyDataResetPlanner.IsCompanyColumn(c.column_name)).Select(c => c.column_name).ToList(),
                g.Select(c => c.column_name).ToList()))
            .ToList();

        var keys = fks.Select(f => new CatalogForeignKey(f.child, f.child_col, f.parent, f.parent_col, f.del_rule.Length > 0 ? f.del_rule[0] : 'a')).ToList();
        return new CompanyDataCatalog(tables, keys);
    }

    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        // REPEATABLE READ : les témoins comptés avant et après voient le même instantané, une
        // écriture concurrente d'une autre société ne déclenche pas le garde-fou à tort.
        _tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        // Borné : une société avec GPS peut porter beaucoup d'alertes, mais on ne bloque pas
        // l'ingestion indéfiniment — un verrou tenu par un autre fait échouer (et annuler) plutôt qu'attendre.
        await _context.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = '300s'", ct);
        await _context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
    }

    public async Task CommitAsync(CancellationToken ct)
    {
        if (_tx is null) return;
        await _tx.CommitAsync(ct);
        await _tx.DisposeAsync();
        _tx = null;
    }

    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_tx is null) return;
        await _tx.RollbackAsync(ct);
        await _tx.DisposeAsync();
        _tx = null;
    }

    public Task<int> ExecuteAsync(string sql, int companyId, CancellationToken ct)
        => _context.Database.ExecuteSqlRawAsync(sql, new object[] { companyId }, ct);

    public async Task<long> CountAsync(string quotedTable, string where, int companyId, CancellationToken ct)
        => await _context.Database.SqlQueryRaw<long>($"SELECT count(*) AS \"Value\" FROM {quotedTable} WHERE {where}", companyId).SingleAsync(ct);

    public async Task<IReadOnlyList<string>> SelectStringsAsync(string quotedTable, string quotedColumn, string where, int companyId, CancellationToken ct)
        => await _context.Database.SqlQueryRaw<string>($"SELECT {quotedColumn}::text AS \"Value\" FROM {quotedTable} WHERE ({where}) AND {quotedColumn} IS NOT NULL", companyId).ToListAsync(ct);

    public async Task<int> RevokeDriverAccountsAsync(int companyId, CancellationToken ct)
    {
        // Ordres SQL (et non SaveChanges) : c'est ce qui s'exécute dans la transaction ouverte
        // par BeginTransactionAsync, comme les DELETE du plan.
        var accounts = 0;
        foreach (var statement in DriverRevocationStatements(_context.Model))
            accounts = await _context.Database.ExecuteSqlRawAsync(statement, new object[] { companyId }, ct);
        return accounts;   // le dernier ordre est celui des comptes
    }

    /// <summary>
    /// Les trois ordres de <see cref="RevokeDriverAccountsAsync"/> ({0} = société), le compte en
    /// DERNIER pour que son nombre de lignes soit rendu. Noms de tables et de colonnes lus dans le
    /// modèle EF plutôt qu'écrits à la main : la casse de ces trois tables est incohérente
    /// (refresh_tokens en PascalCase, users et user_device_tokens en snake_case), et le modèle est
    /// ce qui s'exécute déjà en production pour la même révocation (DriverAccountRules).
    /// </summary>
    public static IReadOnlyList<string> DriverRevocationStatements(Microsoft.EntityFrameworkCore.Metadata.IModel model)
    {
        var (users, u) = Map<GisAPI.Domain.Entities.User>(model);
        var (sessions, s) = Map<GisAPI.Domain.Entities.RefreshToken>(model);
        var (devices, d) = Map<GisAPI.Domain.Entities.UserDeviceToken>(model);

        var driver = GisAPI.Domain.Entities.UserAccountTypes.Driver;
        var driversOfCompany = $"{u("CompanyId")} = {{0}} AND {u("AccountType")} = '{driver}'";
        var driverIds = $"SELECT {u("Id")} FROM {users} WHERE {driversOfCompany}";

        return new[]
        {
            $"UPDATE {sessions} SET {s("RevokedAt")} = CURRENT_TIMESTAMP WHERE {s("RevokedAt")} IS NULL AND {s("UserId")} IN ({driverIds})",
            $"UPDATE {devices} SET {d("IsActive")} = FALSE WHERE {d("IsActive")} = TRUE AND {d("UserId")} IN ({driverIds})",
            $"UPDATE {users} SET {u("Status")} = 'inactive', {u("UpdatedAt")} = CURRENT_TIMESTAMP WHERE {driversOfCompany}",
        };
    }

    private static (string Table, Func<string, string> Column) Map<T>(Microsoft.EntityFrameworkCore.Metadata.IModel model)
    {
        var entity = model.FindEntityType(typeof(T))
            ?? throw new InvalidOperationException($"{typeof(T).Name} absent du modèle EF");
        var table = entity.GetTableName()!;
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema());
        return (CompanyDataResetPlanner.Q(table), property =>
            CompanyDataResetPlanner.Q(entity.FindProperty(property)?.GetColumnName(store)
                ?? throw new InvalidOperationException($"{typeof(T).Name}.{property} absent du modèle EF")));
    }
}
