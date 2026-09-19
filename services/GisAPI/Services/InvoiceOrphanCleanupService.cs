using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Borne la place disque prise par le scan de facture. Le fichier est stocké sous
/// <c>uploads/invoices/{companyId}/{guid}{ext}</c> DÈS l'envoi, avant que
/// l'utilisateur ne décide d'enregistrer quoi que ce soit : une revue abandonnée
/// (ou une ligne supprimée plus tard) laisse un fichier que plus rien ne référence.
/// Ce service les supprime, passé le délai de grâce. Un fichier encore référencé
/// n'est JAMAIS touché.
///
/// <para><b>Relecture du 19/09/2026.</b> Ce service a été écrit quand le scan
/// n'existait QUE sur l'écran Dépenses : il ne regardait que
/// <see cref="Domain.Entities.VehicleCost.ReceiptUrl"/>. Le scan est depuis une brique
/// partagée (Carburant, Entretien effectué, Nouvelle réparation, Échéances) et chaque
/// écran range le justificatif AILLEURS — faute de colonne dédiée, deux d'entre eux le
/// rangent dans leurs NOTES. Leurs factures scannées étaient donc détruites le
/// lendemain, en silence, la ligne restant en base avec un lien mort. D'où
/// <see cref="UrlsReferenceesAsync"/> : l'ensemble des URL référencées couvre
/// désormais TOUS les porteurs connus, colonne dédiée comme texte libre.</para>
/// </summary>
public class InvoiceOrphanCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>
    /// Délai de grâce : un fichier plus récent que cela n'est jamais supprimé, même
    /// référencé nulle part — c'est la revue en cours, à l'écran, pas encore enregistrée.
    /// </summary>
    internal static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    /// <summary>Préfixe public des fichiers de ce dossier — la marque qui les distingue
    /// des autres justificatifs (uploads/acquisition-receipts, uploads/documents…).</summary>
    internal const string PrefixeInvoices = "/uploads/invoices/";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<InvoiceOrphanCleanupService> _logger;

    public InvoiceOrphanCleanupService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env,
        ILogger<InvoiceOrphanCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so it doesn't compete with migrations/seed.
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Invoice orphan sweep failed"); }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        // IGisDbContext plutôt que GisDbContext : la DI rend la même instance scopée
        // (DependencyInjection.cs), et le balayage devient testable sur TestGisDbContext.
        var db = scope.ServiceProvider.GetRequiredService<IGisDbContext>();

        var deleted = await BalayerAsync(db, _env.ContentRootPath, DateTime.UtcNow - GracePeriod, _logger, ct);
        if (deleted > 0)
            _logger.LogInformation("Invoice orphan sweep: deleted {Count} unreferenced file(s)", deleted);
    }

    /// <summary>
    /// Supprime les fichiers de <c>uploads/invoices</c> que plus aucun porteur ne
    /// référence et qui sont antérieurs à <paramref name="limiteDeGrace"/> ; rend le
    /// nombre de fichiers supprimés. Extrait du service pour qu'un test puisse le
    /// dérouler sur un dossier temporaire, sans hébergeur ni minuterie.
    /// </summary>
    internal static async Task<int> BalayerAsync(IGisDbContext db, string contentRootPath,
        DateTime limiteDeGrace, ILogger? logger, CancellationToken ct)
    {
        var uploads = Path.Combine(contentRootPath, "uploads");
        var racine = Path.Combine(uploads, "invoices");
        if (!Directory.Exists(racine)) return 0;

        var keep = await UrlsReferenceesAsync(db, ct);

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(racine, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) > limiteDeGrace) continue; // dans le délai de grâce

            // URL publique que ce fichier porterait : /uploads/invoices/<société>/<nom>
            var rel = "/uploads/" + Path.GetRelativePath(uploads, file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (keep.Contains(rel)) continue;

            try { File.Delete(file); deleted++; }
            catch (Exception ex) { logger?.LogWarning(ex, "Could not delete orphan invoice file {File}", file); }
        }

        return deleted;
    }

    /// <summary>
    /// Toutes les URL de <c>uploads/invoices</c> qu'une ligne enregistrée référence, quel
    /// que soit l'écran qui l'a enregistrée. Un fichier absent de cet ensemble est un
    /// orphelin — c'est donc ICI que se joue la sécurité du balayage, et un porteur
    /// oublié détruit la facture du client.
    ///
    /// <para>Porteurs, écran par écran (19/09/2026) :</para>
    /// <list type="bullet">
    ///   <item><b>Dépenses</b> et <b>Échéances</b> (renouvellement) → <c>vehicle_costs.receipt_url</c>,
    ///     la seule colonne prévue pour cela. Réserve connue : un renouvellement SANS montant
    ///     ne crée aucune dépense (règle du 26/08/2026), donc n'enregistre l'URL nulle part —
    ///     la quittance scannée reste orpheline, à corriger côté RenewDocumentCommandHandler ;</item>
    ///   <item><b>Carburant</b> → <c>fuel_entries.notes</c> (« Ticket scanné : … ») : la table
    ///     n'a pas de colonne justificatif ;</item>
    ///   <item><b>Nouvelle réparation</b> → <c>repairs.notes</c> (« Justificatif : … ») : même raison ;</item>
    ///   <item><b>Entretien effectué</b> → NULLE PART à ce jour : la fenêtre affiche le lien
    ///     mais <c>MarkDoneRequest</c> ne le transmet pas. <c>maintenance_logs.notes</c> est lu
    ///     ci-dessous : le jour où l'écran l'y range, la facture est protégée sans rien changer ici.</item>
    /// </list>
    /// <para>Les autres colonnes lues ci-dessous sont de la même nature (une URL de
    /// justificatif) et ne coûtent qu'une requête toutes les 6 heures : elles ne peuvent
    /// que RETENIR un fichier, jamais en faire supprimer un de plus. C'est le sens de ce
    /// filet — le jour où un écran y range un scan, rien n'est détruit en silence.</para>
    ///
    /// <para>Filtres de tenance IGNORÉS : le balayage tourne hors requête HTTP, donc sans
    /// société courante ; sans <c>IgnoreQueryFilters</c> chaque requête rendrait zéro ligne
    /// et TOUT serait considéré orphelin.</para>
    /// </summary>
    internal static async Task<HashSet<string>> UrlsReferenceesAsync(IGisDbContext db, CancellationToken ct)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task AjouterAsync(IQueryable<string?> valeurs)
        {
            foreach (var texte in await valeurs.ToListAsync(ct))
                foreach (var url in UrlsDansLeTexte(texte))
                    keep.Add(url);
        }

        // ── Colonnes dédiées ──────────────────────────────────────────────────
        await AjouterAsync(db.VehicleCosts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.ReceiptUrl != null && c.ReceiptUrl.Contains(PrefixeInvoices))
            .Select(c => c.ReceiptUrl));

        await AjouterAsync(db.AcquisitionPayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.ReceiptUrl != null && p.ReceiptUrl.Contains(PrefixeInvoices))
            .Select(p => p.ReceiptUrl));

        await AjouterAsync(db.MaintenanceRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.InvoiceUrl != null && m.InvoiceUrl.Contains(PrefixeInvoices))
            .Select(m => m.InvoiceUrl));

        await AjouterAsync(db.VehicleDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.FileUrl != null && d.FileUrl.Contains(PrefixeInvoices))
            .Select(d => d.FileUrl));

        // ── Texte libre : le lien est noyé dans une phrase, faute de colonne ───
        await AjouterAsync(db.FuelEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.Notes != null && f.Notes.Contains(PrefixeInvoices))
            .Select(f => f.Notes));

        await AjouterAsync(db.Repairs.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Notes != null && r.Notes.Contains(PrefixeInvoices))
            .Select(r => r.Notes));

        await AjouterAsync(db.MaintenanceLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Notes != null && l.Notes.Contains(PrefixeInvoices))
            .Select(l => l.Notes));

        await AjouterAsync(db.VehicleCosts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Notes != null && c.Notes.Contains(PrefixeInvoices))
            .Select(c => c.Notes));

        return keep;
    }

    /// <summary>
    /// URL de <c>uploads/invoices</c> contenues dans un texte — la colonne peut être
    /// l'URL seule (<c>receipt_url</c>) comme une phrase qui l'entoure (« Ticket scanné :
    /// /uploads/invoices/7/ab….jpg »). L'URL s'arrête au premier caractère qui ne peut
    /// pas appartenir à un chemin (espace, guillemet, parenthèse, virgule…), et la
    /// ponctuation finale d'une phrase est retirée : le nom stocké se termine toujours
    /// par son extension. (Interne pour les tests.)
    /// </summary>
    internal static IEnumerable<string> UrlsDansLeTexte(string? texte)
    {
        if (string.IsNullOrEmpty(texte)) yield break;

        var i = texte.IndexOf(PrefixeInvoices, StringComparison.OrdinalIgnoreCase);
        while (i >= 0)
        {
            var fin = i + PrefixeInvoices.Length;
            while (fin < texte.Length && !EstUneFinDUrl(texte[fin])) fin++;

            var url = texte[i..fin].TrimEnd('.', ',', ';', ':', '!', '?');
            if (url.Length > PrefixeInvoices.Length) yield return url;

            i = texte.IndexOf(PrefixeInvoices, fin, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool EstUneFinDUrl(char c) =>
        char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>' or '(' or ')' or '[' or ']' or ',' or ';';
}
