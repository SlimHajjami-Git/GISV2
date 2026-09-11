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
}
