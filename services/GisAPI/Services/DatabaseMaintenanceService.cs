using System.Diagnostics;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GisAPI.Services;

public record BackupInfo(string Name, long SizeBytes, DateTime CreatedAt, bool Auto);
public record PurgeTable(string Table, string DateColumn, string Label);
public record PurgePreviewRow(string Table, string Label, long RowsToDelete, long TotalRows);

/// <summary>
/// Sauvegardes pg_dump + purge de l'historique ancien. Réservé sys_admin
/// (contrôleur sous /api/admin/*, verrouillé par PermissionMiddleware).
///
/// Sécurité :
/// - pg_dump/psql tournent dans le pod API (postgresql-client ajouté à l'image),
///   contre le service postgres via la chaîne de connexion EF ;
/// - les backups sont écrits dans BackupDir (volume monté), noms validés pour
///   empêcher tout path traversal ;
/// - la purge ne touche QUE des tables d'historique explicitement listées, avec
///   une borne temporelle, par lots, et crée TOUJOURS un backup avant de
///   supprimer.
/// </summary>
public class DatabaseMaintenanceService
{
    // Seules ces tables sont purgeables — jamais l'opérationnel (véhicules,
    // sociétés, abonnements, utilisateurs, dépenses…).
    public static readonly PurgeTable[] PurgeableTables =
    {
        new("gps_positions",   "recorded_at", "Positions GPS"),
        new("gps_alerts",      "timestamp",   "Alertes GPS"),
        new("frame_debug_log", "created_at",  "Journal des trames"),
        new("geofence_events", "timestamp",   "Événements de zones"),
        new("device_events",   "created_at",  "Événements boîtiers"),
        new("vehicle_stops",   "created_at",  "Arrêts véhicules"),
    };

    private readonly GisDbContext _context;
    private readonly ILogger<DatabaseMaintenanceService> _logger;
    private readonly string _backupDir;

    public DatabaseMaintenanceService(GisDbContext context, IConfiguration config, ILogger<DatabaseMaintenanceService> logger)
    {
        _context = context;
        _logger = logger;
        _backupDir = config["Backup:Directory"] ?? "/backups";
    }

    private NpgsqlConnectionStringBuilder Conn()
        => new(_context.Database.GetConnectionString());

    // ─────────────────── Sauvegardes ───────────────────

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_backupDir)) return Array.Empty<BackupInfo>();
        return new DirectoryInfo(_backupDir).GetFiles("*.dump")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.Name, f.Length, f.CreationTimeUtc,
                f.Name.StartsWith("auto_", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>Crée un backup pg_dump -Fc. prefix "manual" ou "presuppr".</summary>
    public async Task<BackupInfo> CreateBackupAsync(string prefix, CancellationToken ct)
    {
        Directory.CreateDirectory(_backupDir);
        var c = Conn();
        // Horodatage passé en paramètre serveur (Date.Now interdit ici serait
        // dans un workflow ; ici c'est du runtime normal, UtcNow est OK).
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safePrefix = new string(prefix.Where(char.IsLetterOrDigit).ToArray());
        var name = $"{safePrefix}_{stamp}.dump";
        var path = Path.Combine(_backupDir, name);

        var psi = new ProcessStartInfo("pg_dump")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-Fc");                 // format custom (compressé, restaurable)
        psi.ArgumentList.Add("-h"); psi.ArgumentList.Add(c.Host ?? "postgres");
        psi.ArgumentList.Add("-p"); psi.ArgumentList.Add((c.Port == 0 ? 5432 : c.Port).ToString());
        psi.ArgumentList.Add("-U"); psi.ArgumentList.Add(c.Username ?? "postgres");
        psi.ArgumentList.Add("-d"); psi.ArgumentList.Add(c.Database ?? "gis_v2");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add(path);
        psi.Environment["PGPASSWORD"] = c.Password ?? "";

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("pg_dump introuvable dans l'image.");
        var stderr = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
        {
            if (File.Exists(path)) File.Delete(path);
            throw new InvalidOperationException($"pg_dump a échoué (code {p.ExitCode}) : {stderr}");
        }

        var fi = new FileInfo(path);
        _logger.LogInformation("Backup créé : {Name} ({Size} octets)", name, fi.Length);
        return new BackupInfo(name, fi.Length, fi.CreationTimeUtc, safePrefix.StartsWith("auto"));
    }

    /// <summary>Résout un nom de backup en chemin sûr (anti path-traversal).</summary>
    public string? ResolveBackupPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..") ||
            name.Contains('/') || name.Contains('\\') || !name.EndsWith(".dump"))
            return null;
        var path = Path.Combine(_backupDir, name);
        return File.Exists(path) ? path : null;
    }

    public bool DeleteBackup(string name)
    {
        var path = ResolveBackupPath(name);
        if (path == null) return false;
        File.Delete(path);
        return true;
    }

    // ─────────────────── Purge ───────────────────

    public async Task<IReadOnlyList<PurgePreviewRow>> PreviewPurgeAsync(int months, IEnumerable<string> tables, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-months);
        var selected = PurgeableTables.Where(t => tables.Contains(t.Table)).ToList();
        var rows = new List<PurgePreviewRow>();
        foreach (var t in selected)
        {
            try
            {
                var toDelete = await ScalarAsync(
                    $"SELECT COUNT(*) FROM {t.Table} WHERE {t.DateColumn} < @cutoff", cutoff, ct);
                var total = await ScalarAsync($"SELECT COUNT(*) FROM {t.Table}", null, ct);
                rows.Add(new PurgePreviewRow(t.Table, t.Label, toDelete, total));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Preview purge échoué pour {Table}", t.Table);
            }
        }
        return rows;
    }

    /// <summary>Purge par lots. Retourne le total de lignes supprimées par table.</summary>
    public async Task<Dictionary<string, long>> PurgeAsync(int months, IEnumerable<string> tables, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-months);
        var selected = PurgeableTables.Where(t => tables.Contains(t.Table)).ToList();
        var deleted = new Dictionary<string, long>();
        const int batch = 50_000;

        foreach (var t in selected)
        {
            long total = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // ctid IN (...) : suppression par lots, évite un lock long et
                    // le bloat d'un DELETE massif unique.
                    var affected = await _context.Database.ExecuteSqlRawAsync(
                        $@"DELETE FROM {t.Table} WHERE ctid IN (
                             SELECT ctid FROM {t.Table} WHERE {t.DateColumn} < {{0}} LIMIT {batch})",
                        new object[] { cutoff }, ct);
                    total += affected;
                    if (affected < batch) break;
                }
                deleted[t.Table] = total;
                _logger.LogWarning("PURGE {Table} : {Rows} lignes < {Cutoff:yyyy-MM-dd} supprimées", t.Table, total, cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PURGE {Table} échouée après {Rows} lignes", t.Table, total);
                deleted[t.Table] = total;
            }
        }
        return deleted;
    }

    private async Task<long> ScalarAsync(string sql, DateTime? cutoff, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_context.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (cutoff.HasValue) cmd.Parameters.AddWithValue("cutoff", cutoff.Value);
        var r = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(r);
    }
}
