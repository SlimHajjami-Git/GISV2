using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Suppression définitive d'un dossier de sinistre (demande du 18/09/2026) :
/// l'écran Sinistres n'avait aucun moyen de retirer un dossier ouvert par erreur
/// ou en double.
///
/// <para>Les règles, décidées avec le client :</para>
/// <list type="bullet">
///   <item>L'argent reste — la réparation née de la phase 5 (<c>repairs</c>) et le
///     remboursement d'assurance (<c>vehicle_costs</c>) sont DÉTACHÉS, jamais
///     supprimés : ils restent visibles dans Réparations et dans Dépenses, comme le
///     fait déjà la base (ON DELETE SET NULL).</item>
///   <item>Les pièces du dossier partent — documents et tiers (cascade), ainsi que
///     les fichiers correspondants sous <c>uploads/</c>, comme la suppression d'un
///     document unitaire.</item>
///   <item>Les notifications du dossier partent aussi : elles ne portent aucune clé
///     étrangère, la cloche garderait sinon une entrée menant à un rapport vide.</item>
///   <item>La route <c>/api/accident-reports</c> n'est couverte par aucune clé du
///     PermissionMiddleware : la suppression porte donc sa propre garde (admin de
///     société ou droit Sinistres), sinon 403.</item>
///   <item>Portée véhicules : on ne supprime que ce qu'on peut ouvrir (404 sinon).</item>
///   <item>Un dossier détecté il y a moins de 30 minutes est refusé (409) : la
///     détection le recréerait. « Fausse alerte » est le bon geste.</item>
/// </list>
///
/// <paramref name="UploadsRoot"/> est le dossier physique servi sous <c>/uploads</c>
/// (null = ne pas toucher aux fichiers) — même convention que la remise à zéro d'une
/// société, la couche Application n'ayant pas accès à l'hébergement web.
/// </summary>
public record DeleteAccidentEventCommand(int AccidentEventId, string? UploadsRoot)
    : IRequest<DeleteAccidentEventResult>;

/// <param name="Reference">Référence du dossier (<c>reference_code</c> ou <c>#id</c>), pour le message de retour.</param>
/// <param name="DeletedNotifications">Entrées de la cloche retirées avec le dossier.</param>
/// <param name="DetachedCosts">Dépenses conservées, désormais sans lien avec le dossier.</param>
/// <param name="DetachedRepairs">Réparations conservées dans l'écran Réparations, détachées du dossier.</param>
public record DeleteAccidentEventResult(
    int Id,
    string Reference,
    int DeletedDocuments,
    int DeletedThirdParties,
    int DeletedNotifications,
    int DetachedCosts,
    int DeletedFiles,
    int DetachedRepairs);

public class DeleteAccidentEventCommandHandler : IRequestHandler<DeleteAccidentEventCommand, DeleteAccidentEventResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<DeleteAccidentEventCommandHandler> _logger;

    public DeleteAccidentEventCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenant,
        ILogger<DeleteAccidentEventCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<DeleteAccidentEventResult> Handle(DeleteAccidentEventCommand request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId
            ?? throw new DomainException("Société non identifiée");

        await EnsureCanDeleteAsync(companyId, ct);

        // Société de l'appelant, puis portée véhicules : la même règle que la fiche
        // (GetAccidentReportQueryHandler). Un employé restreint à quelques véhicules
        // recevait 404 en ouvrant le dossier d'un autre véhicule mais pouvait le
        // SUPPRIMER (revue de l'intégration du 18/09/2026). Hors portée = introuvable,
        // même réponse qu'un dossier inexistant ; un dossier sans véhicule n'est
        // visible, donc supprimable, que par un administrateur.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);
        var query = _context.AccidentEvents
            .Include(e => e.Documents)
            .Include(e => e.ThirdParties)
            .Where(e => e.Id == request.AccidentEventId && e.CompanyId == companyId);
        if (scope is not null)
            query = query.Where(e => e.VehicleId != null && scope.Contains(e.VehicleId.Value));

        var ev = await query.FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Dossier de sinistre introuvable.");

        // Un dossier DÉTECTÉ reste dans la fenêtre de scan d'AccidentDetectionService
        // (trames de 10 à 25 minutes) : supprimé, il serait recréé sous un nouvel id au
        // scan suivant, et la notification et l'e-mail d'alerte repartiraient vers toute
        // la société. « Fausse alerte » garde la ligne au statut dismissed, que la
        // détection reconnaît. Refus (409) avant toute écriture.
        if (PeutEtreRedetecte(ev.Origin, ev.IncidentAt, DateTime.UtcNow))
            throw new ConflictException(
                "Ce dossier a été détecté automatiquement il y a moins de "
                + $"{FenetreRedetectionMinutes} minutes : supprimé maintenant, il serait recréé par la détection "
                + "et l'alerte repartirait. Utilisez « Fausse alerte » pour l'écarter, ou supprimez-le plus tard.");

        var reference = ev.ReferenceCode is { Length: > 0 } code ? code : $"#{ev.Id}";

        // Fichiers relevés AVANT la suppression des lignes : après, plus rien ne les
        // référencerait et ils resteraient sur le disque du nœud. Les zones endommagées
        // ne sont PAS lues : ce sont des libellés ("arriere", "coffre") saisis par le
        // client, jamais des fichiers — les y chercher permettait d'effacer un fichier
        // d'un autre dossier (« …/12/../../invoices/… »). Les pièces vivent dans Documents.
        var fichiers = new List<string>();
        fichiers.AddRange(ResetCompanyDataCommandHandler.ExtractUploadPaths(ev.PdfReportUrl));
        foreach (var doc in ev.Documents)
            fichiers.AddRange(ResetCompanyDataCommandHandler.ExtractUploadPaths(doc.FileUrl));

        // Même garde que la suppression d'un document unitaire : on n'efface que sous le
        // dossier de CE sinistre. Un chemin venu d'ailleurs est signalé, jamais supprimé.
        var prefixe = $"/uploads/accident-reports/{ev.Id}/";
        var horsDossier = fichiers.Where(f => !EstDansLeDossier(f, prefixe)).ToList();
        if (horsDossier.Count > 0)
            _logger.LogWarning(
                "Sinistre #{AccidentId} : {Count} fichier(s) hors de {Prefixe} conservés sur le disque : {Fichiers}",
                ev.Id, horsDossier.Count, prefixe, string.Join(", ", horsDossier));
        fichiers = fichiers.Where(f => EstDansLeDossier(f, prefixe)).ToList();

        // L'argent reste : la dépense de réparation ou le remboursement d'assurance
        // survit au dossier, simplement détaché. C'est aussi ce que ferait la base
        // (ON DELETE SET NULL) ; on l'écrit ici pour ne dépendre d'aucune cascade.
        var couts = await _context.VehicleCosts
            .Where(c => c.AccidentEventId == ev.Id)
            .ToListAsync(ct);
        foreach (var cout in couts) cout.AccidentEventId = null;

        // Même règle pour la réparation née de la phase 5 (migration 049) : elle reste
        // dans l'écran Réparations et dans les rapports, simplement détachée. Écrit ici
        // plutôt que laissé à la cascade ON DELETE SET NULL de la base.
        // Filtre société EXPLICITE : repairs n'a pas de filtre de requête global, sa clé
        // de cloisonnement est societe_id (même précaution que l'agrégateur des coûts).
        var reparations = await _context.Repairs
            .Where(r => r.AccidentEventId == ev.Id && r.SocieteId == companyId)
            .ToListAsync(ct);
        foreach (var reparation in reparations) reparation.AccidentEventId = null;

        // Les notifications du dossier n'ont aucune clé étrangère : sans ce retrait, la
        // cloche garderait « Accident détecté » et mènerait à un rapport sans données.
        var notifications = await _context.Notifications
            .Where(n => n.CompanyId == companyId
                && n.ReferenceType == "accident_event"
                && n.ReferenceId == ev.Id)
            .ToListAsync(ct);
        _context.Notifications.RemoveRange(notifications);

        var documents = ev.Documents.Count;
        var tiers = ev.ThirdParties.Count;
        _context.AccidentEventDocuments.RemoveRange(ev.Documents);
        _context.AccidentEventThirdParties.RemoveRange(ev.ThirdParties);
        _context.AccidentEvents.Remove(ev);
        await _context.SaveChangesAsync(ct);

        // Les fichiers après la base, comme la suppression d'un document : une ligne
        // conservée avec un fichier manquant serait un lien mort dans la chronologie.
        var fichiersSupprimes = ResetCompanyDataCommandHandler.DeleteFiles(request.UploadsRoot, fichiers);
        SupprimerDossierVide(request.UploadsRoot, ev.Id);

        _logger.LogWarning(
            "Sinistre {Reference} (#{AccidentId}, société {CompanyId}) supprimé par l'utilisateur {UserId} : "
            + "{Documents} document(s), {Tiers} tiers, {Notifications} notification(s), {Fichiers} fichier(s) ; "
            + "{Couts} dépense(s) et {Reparations} réparation(s) conservées et détachées.",
            reference, ev.Id, companyId, _tenant.UserId, documents, tiers, notifications.Count, fichiersSupprimes,
            couts.Count, reparations.Count);

        return new DeleteAccidentEventResult(
            ev.Id, reference, documents, tiers, notifications.Count, couts.Count, fichiersSupprimes, reparations.Count);
    }

    /// <summary>
    /// Fenêtre pendant laquelle un dossier détecté peut être recréé : la détection relit
    /// les trames jusqu'à 25 minutes en arrière, plus une marge. Même valeur que l'écran
    /// (liste et fiche des sinistres, <c>peutEtreRedetecte</c>).
    /// </summary>
    public const int FenetreRedetectionMinutes = 30;

    /// <summary>Dossier automatique dont l'incident est encore dans la fenêtre de scan.</summary>
    public static bool PeutEtreRedetecte(string? origin, DateTime incidentAtUtc, DateTime nowUtc)
    {
        if (string.Equals(origin, "manual", StringComparison.OrdinalIgnoreCase)) return false;
        var age = nowUtc - incidentAtUtc;
        return age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(FenetreRedetectionMinutes);
    }

    /// <summary>
    /// Le fichier est-il bien SOUS le dossier de ce sinistre ? Un simple StartsWith sur la
    /// chaîne brute laissait passer « /uploads/accident-reports/12/../../invoices/x.pdf »,
    /// que Path.GetFullPath résout hors du dossier. On refuse donc tout segment « . » ou
    /// « .. » et tout séparateur Windows après le préfixe.
    /// </summary>
    internal static bool EstDansLeDossier(string url, string prefixe)
    {
        if (!url.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)) return false;
        var reste = url[prefixe.Length..].Split('?')[0];
        if (reste.Length == 0 || reste.Contains('\\')) return false;
        return reste.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    /// <summary>
    /// Retire le répertoire du dossier une fois vide : sinon chaque suppression laisse
    /// un dossier vide de plus sous <c>uploads/accident-reports/</c>.
    /// </summary>
    private static void SupprimerDossierVide(string? uploadsRoot, int accidentId)
    {
        if (string.IsNullOrEmpty(uploadsRoot)) return;
        try
        {
            var root = Path.GetFullPath(uploadsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var dossier = Path.GetFullPath(Path.Combine(root, "accident-reports", accidentId.ToString()));
            if (!dossier.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            if (!Directory.Exists(dossier)) return;
            if (Directory.EnumerateFileSystemEntries(dossier).Any()) return;
            Directory.Delete(dossier);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Garde propre à la suppression : le contrôleur des sinistres n'est visé par aucune
    /// clé du PermissionMiddleware, tout compte connecté de la société peut l'appeler.
    /// Droits relus en base (jamais les claims), comme le tableau de bord GPA.
    /// </summary>
    private async Task EnsureCanDeleteAsync(int companyId, CancellationToken ct)
    {
        if (_tenant.IsSystemAdmin) return;

        var userId = _tenant.UserId ?? 0;
        var user = userId > 0
            ? await _context.Users.AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            : null;

        var autorise = user is not null
            && (user.Role?.IsSystemRole == true
                || user.Role?.IsCompanyAdmin == true
                || user.AccessLevel == "admin"
                || user.CanAccidents);

        if (!autorise)
            throw new ForbiddenAccessException(
                "Suppression refusée : seul un administrateur de la société ou un utilisateur "
                + "ayant le droit Sinistres peut supprimer un dossier de sinistre.");
    }
}
