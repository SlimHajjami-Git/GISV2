using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;

/// <summary>
/// Remise à zéro d'une société par l'administrateur système : supprime TOUT son contenu
/// (véhicules et tout ce qui s'y rattache, dépenses, carburant, entretien, réparations, accidents,
/// conducteurs, fournisseurs, géofences, trajets, notifications, journal d'audit, fichiers) en
/// conservant la société, son abonnement, ses utilisateurs et rôles, ses boîtiers et leurs positions.
/// <paramref name="ConfirmName"/> doit être le nom exact de la société (saisi par l'admin).
/// <paramref name="UploadsRoot"/> est le dossier physique servi sous /uploads (null = ne pas toucher aux fichiers).
/// <paramref name="DryRun"/> : l'aperçu chiffré — de simples comptages, aucune transaction, aucun verrou ;
/// le nom n'est alors pas exigé.
/// </summary>
public record ResetCompanyDataCommand(int CompanyId, string ConfirmName, string? UploadsRoot, bool DryRun = false) : IRequest<ResetCompanyDataResult>;

public record ResetTableCount(string Table, int Rows);

public record ResetCompanyDataResult(
    int CompanyId,
    string CompanyName,
    IReadOnlyList<ResetTableCount> Deleted,
    int TotalRows,
    int FilesDeleted,
    IReadOnlyList<string> Kept,
    long DurationMs,
    bool DryRun);

public class ResetCompanyDataCommandHandler : IRequestHandler<ResetCompanyDataCommand, ResetCompanyDataResult>
{
    private readonly IGisDbContext _context;
    private readonly ICompanyDataStore _store;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<ResetCompanyDataCommandHandler> _logger;

    public ResetCompanyDataCommandHandler(IGisDbContext context, ICompanyDataStore store,
        ICurrentTenantService tenant, ILogger<ResetCompanyDataCommandHandler> logger)
    {
        _context = context;
        _store = store;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<ResetCompanyDataResult> Handle(ResetCompanyDataCommand request, CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        // Le middleware garde déjà /api/admin ; on ne dépend pas de lui pour une suppression.
        if (!_tenant.IsSystemAdmin)
            throw new ForbiddenAccessException("Seul l'administrateur système peut remettre une société à zéro.");

        var societe = await _context.Societes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == request.CompanyId, ct)
            ?? throw new NotFoundException("Societe", request.CompanyId);

        if (!request.DryRun && !string.Equals(societe.Name.Trim(), (request.ConfirmName ?? string.Empty).Trim(), StringComparison.Ordinal))
            throw new DomainException("Le nom saisi ne correspond pas au nom de la société : remise à zéro refusée.");

        var catalog = await _store.LoadCatalogAsync(ct);
        ResetPlan plan;
        try { plan = CompanyDataResetPlanner.Plan(catalog); }
        catch (InvalidOperationException ex)
        {
            // Le schéma a évolué d'une façon que le plan refuse (table protégée en cascade, cycle bloquant) :
            // un message lisible plutôt qu'un 500, et rien n'a été touché.
            throw new DomainException("Remise à zéro impossible avec le schéma actuel : " + ex.Message);
        }
        var cid = request.CompanyId;

        if (request.DryRun)
            return await PreviewAsync(plan, cid, societe.Name, started, ct);

        var deleted = new List<ResetTableCount>();
        var files = new List<string>();

        await _store.BeginTransactionAsync(ct);
        try
        {
            await CheckGuardsAsync(plan, cid, ct);

            // Témoins, lus dans la même transaction (instantané REPEATABLE READ) : ce qui doit rester
            // identique, dans la société et chez les autres.
            var before = await WitnessesAsync(cid, ct);

            foreach (var step in plan.Steps)
            {
                foreach (var col in step.FileColumns)
                {
                    var values = await _store.SelectStringsAsync(Q(step.Table), Q(col), step.Where, cid, ct);
                    files.AddRange(values.SelectMany(ExtractUploadPaths));
                }

                var rows = await _store.ExecuteAsync($"DELETE FROM {Q(step.Table)} WHERE {step.Where}", cid, ct);
                if (rows > 0) deleted.Add(new ResetTableCount(step.Table, rows));
            }

            var after = await WitnessesAsync(cid, ct);
            if (after != before)
                throw new DomainException(
                    $"Garde-fou déclenché (utilisateurs {before.Users}→{after.Users}, rôles {before.Roles}→{after.Roles}, " +
                    $"véhicules des autres sociétés {before.OthersVehicles}→{after.OthersVehicles}, dépenses des autres sociétés " +
                    $"{before.OthersCosts}→{after.OthersCosts}, société {before.Societe}→{after.Societe}) : rien n'a été supprimé.");

            await _store.CommitAsync(ct);
        }
        catch (DomainException)
        {
            await _store.RollbackAsync(ct);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _store.RollbackAsync(ct);
            // Une contrainte de la base (23503…) vaut mieux en clair qu'en 500 anonyme : rien n'a été modifié.
            throw new DomainException("La base a refusé la suppression, rien n'a été modifié : " + ex.GetBaseException().Message);
        }

        var total = deleted.Sum(d => d.Rows);
        var filesDeleted = DeleteFiles(request.UploadsRoot, files);
        var duration = (long)(DateTime.UtcNow - started).TotalMilliseconds;

        // Trace hors société (CompanyId null) : elle survit à une prochaine remise à zéro.
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _tenant.UserId,
            CompanyId = null,
            Action = "company_data_reset",
            EntityType = "Societe",
            EntityId = cid,
            EntityName = societe.Name,
            Description = $"Remise à zéro de la société {societe.Name} (#{cid}) : {total} lignes dans {deleted.Count} tables, {filesDeleted} fichiers.",
            NewValues = deleted.ToDictionary(d => d.Table, d => (object)d.Rows),
            Timestamp = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        _logger.LogWarning("Company {CompanyId} ({Name}) reset by user {UserId}: {Rows} rows in {Tables} tables, {Files} files, {Ms} ms",
            cid, societe.Name, _tenant.UserId, total, deleted.Count, filesDeleted, duration);

        return new ResetCompanyDataResult(cid, societe.Name, deleted, total, filesDeleted, plan.Protected, duration, DryRun: false);
    }

    /// <summary>L'aperçu : comptages seulement, dans l'ordre du plan, plus les fichiers référencés.</summary>
    private async Task<ResetCompanyDataResult> PreviewAsync(ResetPlan plan, int cid, string name, DateTime started, CancellationToken ct)
    {
        await CheckGuardsAsync(plan, cid, ct);

        var counts = new List<ResetTableCount>();
        var files = new List<string>();
        foreach (var step in plan.Steps)
        {
            var n = await _store.CountAsync(Q(step.Table), step.Where, cid, ct);
            if (n > 0) counts.Add(new ResetTableCount(step.Table, (int)n));
            foreach (var col in step.FileColumns)
            {
                var values = await _store.SelectStringsAsync(Q(step.Table), Q(col), step.Where, cid, ct);
                files.AddRange(values.SelectMany(ExtractUploadPaths));
            }
        }

        return new ResetCompanyDataResult(cid, name, counts, counts.Sum(c => c.Rows),
            files.Distinct(StringComparer.OrdinalIgnoreCase).Count(), plan.Protected,
            (long)(DateTime.UtcNow - started).TotalMilliseconds, DryRun: true);
    }

    private async Task CheckGuardsAsync(ResetPlan plan, int cid, CancellationToken ct)
    {
        foreach (var guard in plan.Guards)
        {
            var n = await _store.CountAsync(Q(guard.Table), guard.Where, cid, ct);
            if (n > 0)
                throw new DomainException($"Remise à zéro refusée — {guard.Reason} : {n} ligne(s). À corriger à la main avant de réessayer.");
        }
    }

    private readonly record struct Witnesses(long Users, long Roles, long Societe, long OthersVehicles, long OthersCosts);

    private async Task<Witnesses> WitnessesAsync(int cid, CancellationToken ct) => new(
        await _store.CountAsync(Q("users"), $"{Q("company_id")} = {{0}}", cid, ct),
        await _store.CountAsync(Q("roles"), $"{Q("societe_id")} = {{0}}", cid, ct),
        await _store.CountAsync(Q("societes"), $"{Q("id")} = {{0}}", cid, ct),
        await _store.CountAsync(Q("vehicles"), $"{Q("company_id")} <> {{0}}", cid, ct),
        await _store.CountAsync(Q("vehicle_costs"), $"{Q("company_id")} <> {{0}}", cid, ct));

    private static string Q(string id) => CompanyDataResetPlanner.Q(id);

    /// <summary>Un chemin « /uploads/… » ou un tableau JSON de tels chemins (maintenance_logs.photos).</summary>
    public static IEnumerable<string> ExtractUploadPaths(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        var v = value.Trim();
        if (v.StartsWith('['))
        {
            List<string>? items = null;
            try { items = JsonSerializer.Deserialize<List<string>>(v); } catch (JsonException) { }
            if (items is null) yield break;
            foreach (var item in items) foreach (var p in ExtractUploadPaths(item)) yield return p;
            yield break;
        }
        if (v.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) yield return v;
    }

    /// <summary>Supprime les fichiers sous la racine des uploads, sans jamais en sortir.</summary>
    public static int DeleteFiles(string? uploadsRoot, IEnumerable<string> urls)
    {
        if (string.IsNullOrEmpty(uploadsRoot)) return 0;
        var root = Path.GetFullPath(uploadsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var count = 0;
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var relative = url.Substring("/uploads/".Length).Split('?')[0].Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(full)) continue;
            try { File.Delete(full); count++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return count;
    }
}
