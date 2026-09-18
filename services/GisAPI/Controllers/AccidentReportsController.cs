using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Calypso 7 — single entry point for the unified accident lifecycle.
/// Replaces the old <c>AccidentClaimsController</c> (deleted) — manual
/// claim data was migrated into <c>accident_events</c> by the
/// <c>UnifyAccidentEventLifecycle</c> migration.
///
/// <para>Endpoints map to the timeline phases:</para>
/// <list type="bullet">
///   <item><c>GET    /                          </c> — paged list</item>
///   <item><c>GET    /:id                       </c> — full timeline DTO</item>
///   <item><c>POST   /:id/confirm               </c> — Phase 2 trigger (status → confirmed)</item>
///   <item><c>POST   /:id/dismiss               </c> — false alarm</item>
///   <item><c>POST   /manual                    </c> — create a manually-declared accident</item>
///   <item><c>DELETE /:id                       </c> — suppression définitive du dossier (admin / droit Sinistres)</item>
///   <item><c>PATCH  /:id/initial-damages       </c> — Phase 2 form fields</item>
///   <item><c>PATCH  /:id/expert                </c> — Phase 3</item>
///   <item><c>PATCH  /:id/mechanic-quote        </c> — Phase 4</item>
///   <item><c>PATCH  /:id/repair                </c> — Phase 5 (reporte le coût dans Réparations) ; 200 + avertissement</item>
///   <item><c>PATCH  /:id/claim                 </c> — Phase 6 (reporte le remboursement dans Dépenses) ; 200 + avertissement</item>
///   <item><c>POST   /:id/third-parties         </c> — add a third party</item>
///   <item><c>DELETE /:id/third-parties/:tpId   </c> — remove a third party</item>
///   <item><c>POST   /:id/upload-pdf            </c> — rapport PDF produit par l'application (jamais un document du client)</item>
///   <item><c>GET    /:id/pdf-status            </c> — date de génération du PDF vs dernière modification</item>
///   <item><c>POST   /:id/documents             </c> — multi-file upload typed by phase</item>
///   <item><c>DELETE /:id/documents/:docId      </c> — remove a document</item>
///   <item><c>POST   /simulate                  </c> — debug-only, user 1</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/accident-reports")]
[Authorize]
public class AccidentReportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly GisDbContext _context;
    private readonly INotificationService _notifService;
    private readonly ICurrentTenantService _tenantService;
    private readonly IWebHostEnvironment _env;
    private readonly IManualAccidentEnricher _manualEnricher;
    private readonly ILogger<AccidentReportsController> _logger;

    public AccidentReportsController(
        IMediator mediator,
        GisDbContext context,
        INotificationService notifService,
        ICurrentTenantService tenantService,
        IWebHostEnvironment env,
        IManualAccidentEnricher manualEnricher,
        ILogger<AccidentReportsController> logger)
    {
        _mediator = mediator;
        _context = context;
        _notifService = notifService;
        _tenantService = tenantService;
        _env = env;
        _manualEnricher = manualEnricher;
        _logger = logger;
    }

    // ── Read endpoints ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<ListAccidentEventsResult>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? origin = null,
        [FromQuery] bool includeDismissed = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ListAccidentEventsQuery(page, pageSize, status, vehicleId, origin, includeDismissed),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccidentReportDto>> GetReport(int id, CancellationToken ct)
    {
        var report = await _mediator.Send(new GetAccidentReportQuery(id), ct);
        if (report == null) return NotFound();
        return Ok(report);
    }

    // ── Confirmation / dismissal ───────────────────────────────────────────

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        try { await _mediator.Send(new ConfirmAccidentCommand(id), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        try { await _mediator.Send(new DismissAccidentCommand(id), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
    }

    [HttpPost("manual")]
    public async Task<ActionResult<object>> CreateManual([FromBody] CreateManualAccidentRequest request, CancellationToken ct)
    {
        try
        {
            var id = await _mediator.Send(new CreateManualAccidentCommand(
                VehicleId: request.VehicleId,
                IncidentAt: request.IncidentAt,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                LocationCommune: request.LocationCommune,
                LocationGovernorate: request.LocationGovernorate,
                Description: request.Description,
                Severity: request.Severity,
                EstimatedCost: request.EstimatedCost,
                ClaimNumber: request.ClaimNumber,
                InternalNotes: request.InternalNotes
            ), ct);

            // Fire-and-forget background enrichment: rebuilds story /
            // indicators / reasons / synthesis from the real GPS+MEMS frames
            // around the user-entered IncidentAt, then optionally upgrades
            // the prose via the LLM. The user gets an immediate redirect to
            // /rapport-accident/:id while this runs in the background.
            _ = _manualEnricher.EnrichInBackgroundAsync(id);

            return Ok(new { accidentEventId = id });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Ce que la suppression d'un véhicule emporte : dossiers de sinistre conservés
    /// (détachés), réparations et dépenses supprimées avec lui. Sert la fenêtre de
    /// confirmation de l'écran d'administration des véhicules, qui n'annonçait rien.
    ///
    /// <para>Route sous <c>/api/admin</c> alors que l'action vit ici : l'écran qui
    /// supprime est dans l'espace d'administration, et l'intercepteur Angular n'attache
    /// <c>admin_token</c> qu'aux URL <c>/api/admin</c> (le repli croisé a été retiré,
    /// c'était une faille). Le PermissionMiddleware y exige un administrateur système.</para>
    /// </summary>
    [HttpGet("/api/admin/vehicles/{vehicleId:int}/deletion-impact")]
    public async Task<ActionResult<VehicleDeletionImpactDto>> VehicleDeletionImpact(int vehicleId, CancellationToken ct)
    {
        var impact = await _mediator.Send(new GetVehicleDeletionImpactQuery(vehicleId), ct);
        return impact == null ? NotFound() : Ok(impact);
    }

    // ── Suppression du dossier ─────────────────────────────────────────────

    /// <summary>
    /// Suppression définitive d'un dossier de sinistre (demande du 18/09/2026) :
    /// documents et tiers partent avec lui, fichiers compris ; les dépenses déjà
    /// enregistrées restent dans Dépenses, simplement détachées du dossier.
    /// Garde propre à la route : ce contrôleur n'est visé par aucune clé du
    /// PermissionMiddleware, la commande refuse donc elle-même (403) un compte
    /// sans droit Sinistres ni statut d'administrateur.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var uploadsRoot = Path.Combine(_env.ContentRootPath, "uploads");
        try
        {
            var result = await _mediator.Send(new DeleteAccidentEventCommand(id, uploadsRoot), ct);
            var conserves = new List<string>();
            if (result.DetachedCosts > 0) conserves.Add($"{result.DetachedCosts} dépense(s) restent dans Dépenses");
            if (result.DetachedRepairs > 0) conserves.Add($"{result.DetachedRepairs} réparation(s) restent dans Réparations");
            return Ok(new
            {
                message = conserves.Count > 0
                    ? $"Dossier de sinistre {result.Reference} supprimé. {string.Join(", ", conserves)}."
                    : $"Dossier de sinistre {result.Reference} supprimé.",
                detachedCosts = result.DetachedCosts,
                detachedRepairs = result.DetachedRepairs
            });
        }
        catch (NotFoundException) { return NotFound(new { message = "Dossier de sinistre introuvable." }); }
        // Avant DomainException : ForbiddenAccessException en hérite, l'ordre fait le code HTTP.
        catch (ForbiddenAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Phase commands ─────────────────────────────────────────────────────

    [HttpPatch("{id:int}/initial-damages")]
    public async Task<IActionResult> UpdateInitialDamages(int id, [FromBody] UpdateInitialDamagesRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new UpdateInitialDamagesCommand(id, req.Description, req.Severity,
                req.DamagedZones, req.PoliceReportNumber, req.MileageAtAccident,
                req.WeatherConditions, req.RoadConditions), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/expert")]
    public async Task<IActionResult> RegisterExpert(int id, [FromBody] RegisterExpertAssessmentRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterExpertAssessmentCommand(id, req.VisitedAt, req.ExpertName,
                req.ExpertCompany, req.Assessment, req.EstimatedAmount), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPatch("{id:int}/mechanic-quote")]
    public async Task<IActionResult> RegisterMechanicQuote(int id, [FromBody] RegisterMechanicQuoteRequest req, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new RegisterMechanicQuoteCommand(id, req.QuoteAt, req.MechanicName, req.QuotedAmount), ct);
            return NoContent();
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Phase 5 — suivi de la réparation. Répond 200 avec un avertissement structuré
    /// quand la ligne de réparation n'a PAS pu être écrite (véhicule du dossier
    /// supprimé) : le 204 nu laissait croire que tout avait été reporté.
    /// </summary>
    [HttpPatch("{id:int}/repair")]
    public async Task<ActionResult<object>> RegisterRepair(int id, [FromBody] RegisterRepairRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _mediator.Send(new RegisterRepairCommand(id, req.StartedAt, req.CompletedAt, req.ActualCost), ct);
            return Ok(PhaseResponse(result));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Phase 6 — suivi du sinistre assurance. Même avertissement que la phase 5.</summary>
    [HttpPatch("{id:int}/claim")]
    public async Task<ActionResult<object>> RegisterClaim(int id, [FromBody] RegisterClaimRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _mediator.Send(new RegisterClaimCommand(id, req.ClaimNumber, req.SubmittedAt,
                req.ApprovedAmount, req.Status, req.ThirdPartyInvolved), ct);
            return Ok(PhaseResponse(result));
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Corps commun des phases 5 et 6 : l'écran n'affiche que <c>message</c>.</summary>
    private static object PhaseResponse(PhaseSyncResult result) => new
    {
        message = result.Warning,
        costSynced = result.Synced,
        reason = result.Reason,
    };

    // ── Third parties ──────────────────────────────────────────────────────

    [HttpPost("{id:int}/third-parties")]
    public async Task<ActionResult<object>> AddThirdParty(int id, [FromBody] AddThirdPartyRequest req, CancellationToken ct)
    {
        try
        {
            var tpId = await _mediator.Send(new AddThirdPartyCommand(id, req.Name, req.Phone,
                req.VehiclePlate, req.VehicleModel, req.InsuranceCompany, req.InsuranceNumber, req.InsuranceExpiry), ct);
            return Ok(new { thirdPartyId = tpId });
        }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpDelete("{id:int}/third-parties/{tpId:int}")]
    public async Task<IActionResult> DeleteThirdParty(int id, int tpId, CancellationToken ct)
    {
        try { await _mediator.Send(new DeleteThirdPartyCommand(id, tpId), ct); return NoContent(); }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ── Documents ──────────────────────────────────────────────────────────

    /// <summary>
    /// Rapport PDF PRODUIT par l'application, enregistré dans <c>pdf_report_url</c> —
    /// un seul par dossier, refait à chaque régénération. Un document fourni par le
    /// client (rapport d'expert, devis…) ne passe JAMAIS par ici : il se joint au
    /// dossier par <c>POST /:id/documents</c>, sinon la régénération le remplacerait.
    /// Un dossier antérieur au correctif qui en porte encore un le voit rangé en pièce
    /// jointe avant le remplacement du lien.
    /// </summary>
    [HttpPost("{id:int}/upload-pdf")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<object>> UploadPdf(int id, [FromForm] IFormFile? file, CancellationToken ct)
    {
        var (ev, error) = await ValidateUploadAsync(id, file, allowedExt: ".pdf", maxSize: 20_000_000, ct);
        if (error != null) return error;

        // URL relevée AVANT le remplacement : le PDF est régénéré à chaque
        // enregistrement de phase, et l'ancien fichier restait sur le disque à chaque
        // fois (racine partagée avec le nœud TN). Même règle que la suppression d'un
        // document : on n'efface qu'après avoir enregistré la nouvelle URL.
        var ancienneUrl = ev!.PdfReportUrl;

        // Déclaration MANUELLE antérieure au correctif : pdf_report_url peut porter le
        // document du client, qu'aucune autre ligne ne référence. Il devient une pièce
        // jointe AVANT que le lien ne change, sinon son fichier restait sur le disque
        // sans être atteignable depuis l'écran.
        var archiveEnPieceJointe = await ArchiverPdfDuClientAsync(ev, ancienneUrl, ct);

        var (publicUrl, _) = await SaveFileAsync(id, file!, ".pdf", ct, PrefixeRapportGenere);
        await _mediator.Send(new AttachAccidentPdfCommand(id, publicUrl), ct);
        SupprimerAncienPdf(ev, ancienneUrl, publicUrl, archiveEnPieceJointe);

        return Ok(new { pdfReportUrl = publicUrl, generatedAt = DateTime.UtcNow });
    }

    /// <summary>
    /// Fraîcheur du PDF attaché : date de génération (horodatage du fichier) et date de
    /// dernière modification du dossier. L'écran s'en sert pour dater le lien de
    /// téléchargement et signaler un PDF plus ancien que les informations saisies.
    /// </summary>
    [HttpGet("{id:int}/pdf-status")]
    public async Task<ActionResult<object>> PdfStatus(int id, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;
        if (companyId == null) return BadRequest(new { message = "Société non identifiée" });

        var ev = await _context.AccidentEvents.AsNoTracking()
            .Where(e => e.Id == id && e.CompanyId == companyId.Value)
            .Select(e => new { e.VehicleId, e.PdfReportUrl, e.UpdatedAt })
            .FirstOrDefaultAsync(ct);
        if (ev == null) return NotFound();

        // Même périmètre que la lecture du rapport : un employé restreint à quelques
        // véhicules ne doit pas récupérer l'URL du PDF d'un autre dossier (les fichiers
        // sont servis en statique). Portée nulle = administrateur, tout le parc.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        if (scope is not null && (ev.VehicleId == null || !scope.Contains(ev.VehicleId.Value)))
            return NotFound();

        var generatedAt = DateFichierPdf(id, ev.PdfReportUrl);
        // Tolérance : l'URL est enregistrée juste APRÈS l'écriture du fichier, la date
        // du dossier est donc toujours un peu postérieure à celle du PDF.
        var perime = generatedAt != null && ev.UpdatedAt > generatedAt.Value.AddSeconds(30);

        return Ok(new
        {
            pdfReportUrl = ev.PdfReportUrl,
            generatedAt,
            updatedAt = ev.UpdatedAt,
            stale = perime,
        });
    }

    /// <summary>Date d'écriture du PDF attaché, null s'il n'y en a pas ou si le fichier a disparu.</summary>
    private DateTime? DateFichierPdf(int accidentId, string? fileUrl)
    {
        var chemin = CheminFichierDuDossier(accidentId, fileUrl);
        if (chemin == null || !System.IO.File.Exists(chemin)) return null;
        try { return System.IO.File.GetLastWriteTimeUtc(chemin); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Chemin disque d'un fichier de CE dossier, ou null si l'URL pointe ailleurs :
    /// même garde que la suppression d'un document, on ne sort jamais du dossier.
    /// </summary>
    private string? CheminFichierDuDossier(int accidentId, string? fileUrl)
    {
        var prefixe = $"/uploads/accident-reports/{accidentId}/";
        if (string.IsNullOrEmpty(fileUrl) || !fileUrl.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase)) return null;
        var nom = Path.GetFileName(fileUrl);
        if (string.IsNullOrEmpty(nom)) return null;
        return Path.Combine(_env.ContentRootPath, "uploads", "accident-reports", accidentId.ToString(), nom);
    }

    /// <summary>
    /// Nom des fichiers PRODUITS par l'application, posé depuis le correctif du
    /// 18/09/2026. Les fichiers antérieurs ne le portent pas, quelle que soit leur
    /// origine : il dit « produit par Calypso », jamais « fourni par le client »
    /// (voir <see cref="EstDocumentDuClient"/>).
    /// </summary>
    internal const string PrefixeRapportGenere = "rapport-";

    /// <summary>Ce fichier a-t-il été produit par l'application ?</summary>
    internal static bool EstRapportGenere(string? fileUrl) =>
        Path.GetFileName(fileUrl ?? string.Empty)
            .StartsWith(PrefixeRapportGenere, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// L'ancien <c>pdf_report_url</c> peut-il porter un document REMIS PAR LE CLIENT ?
    ///
    /// <para>Le préfixe ne suffit pas : il n'existe que depuis ce correctif, et tout
    /// l'existant s'appelle « {guid}.pdf » — rapport produit par Calypso compris, puisque
    /// la modale de décision en envoie un à la confirmation de chaque accident détecté.
    /// Seule la déclaration MANUELLE a jamais offert un champ « PDF expert » : sur un
    /// dossier d'origine « auto », l'ancien fichier est forcément un rapport généré.</para>
    /// </summary>
    private static bool EstDocumentDuClient(AccidentEvent ev, string? fileUrl) =>
        !string.IsNullOrWhiteSpace(fileUrl)
        && !EstRapportGenere(fileUrl)
        && string.Equals(ev.Origin, "manual", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Range en pièce jointe le PDF que le dossier portait et que l'application n'a pas
    /// produit (déclaration manuelle antérieure au correctif). Sans cela, la première
    /// régénération — automatique après chaque phase enregistrée — remplaçait le lien et
    /// le document du client n'était plus référencé nulle part. Idempotent : une pièce
    /// jointe portant déjà ce fichier suffit.
    ///
    /// <para>Rend <c>true</c> quand le fichier est désormais référencé par une pièce
    /// jointe : c'est la seule raison de le garder sur le disque, et
    /// <see cref="SupprimerAncienPdf"/> s'y fie au lieu de refaire le test.</para>
    /// </summary>
    private async Task<bool> ArchiverPdfDuClientAsync(AccidentEvent ev, string? ancienneUrl, CancellationToken ct)
    {
        if (!EstDocumentDuClient(ev, ancienneUrl)) return false;

        // Fichier disparu (purge de la racine uploads, remise à zéro d'une société,
        // restauration partielle) : la pièce jointe n'aurait qu'un lien mort, recopié tel
        // quel dans le tableau du PDF remis à l'assureur. Une URL hors du dossier
        // (chemin null) n'est pas jugeable : on la range comme avant.
        var chemin = CheminFichierDuDossier(ev.Id, ancienneUrl);
        if (chemin != null && !System.IO.File.Exists(chemin))
        {
            _logger.LogWarning(
                "UploadPdf: sinistre {AccidentId} — ancien PDF {FileUrl} introuvable sur le disque, aucune pièce jointe créée",
                ev.Id, ancienneUrl);
            return false;
        }

        var dejaJoint = await _context.AccidentEventDocuments
            .AnyAsync(d => d.AccidentEventId == ev.Id && d.FileUrl == ancienneUrl, ct);
        if (dejaJoint) return true;

        // file_name est borné à 300 caractères, file_url à 500 comme pdf_report_url.
        var nom = Path.GetFileName(ancienneUrl!);
        if (nom.Length > 300) nom = nom[..300];
        _context.AccidentEventDocuments.Add(new AccidentEventDocument
        {
            AccidentEventId = ev.Id,
            // Type NEUTRE : le champ de la déclaration s'appelait « PDF expert », mais
            // rien ne prouve que ce fichier-là en soit un. « Rapport d'expertise » serait
            // recopié tel quel dans le PDF remis à l'assureur ; l'utilisateur reclasse.
            DocumentType = "other",
            FileName = string.IsNullOrWhiteSpace(nom) ? "document-du-dossier.pdf" : nom,
            FileUrl = ancienneUrl!,
            MimeType = "application/pdf",
            UploadedByUserId = _tenantService.UserId,
            UploadedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        _logger.LogWarning(
            "UploadPdf: sinistre {AccidentId} — ancien PDF {FileUrl} d'une déclaration manuelle, rangé en pièce jointe avant remplacement du lien",
            ev.Id, ancienneUrl);
        return true;
    }

    /// <summary>
    /// Retire le PDF remplacé, une fois la nouvelle URL enregistrée.
    /// <paramref name="archiveEnPieceJointe"/> vient de <see cref="ArchiverPdfDuClientAsync"/> :
    /// refaire le test ici mentait au journal quand l'archivage avait renoncé.
    /// </summary>
    private void SupprimerAncienPdf(AccidentEvent ev, string? ancienneUrl, string nouvelleUrl, bool archiveEnPieceJointe)
    {
        if (string.IsNullOrEmpty(ancienneUrl) || string.Equals(ancienneUrl, nouvelleUrl, StringComparison.OrdinalIgnoreCase)) return;
        var prefixe = $"/uploads/accident-reports/{ev.Id}/";
        if (!ancienneUrl.StartsWith(prefixe, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("UploadPdf: ancien PDF {FileUrl} hors du dossier {Prefixe}, conservé", ancienneUrl, prefixe);
            return;
        }

        // Seul le document qui vient d'être rangé en pièce jointe est conservé : il est
        // désormais référencé par accident_event_documents. Tout le reste est un rapport
        // remplacé, et le laisser sur le disque faisait grossir les uploads sans fin.
        if (archiveEnPieceJointe)
        {
            _logger.LogWarning(
                "UploadPdf: ancien PDF {FileUrl} conservé sur le disque (sinistre {AccidentId}), il est devenu une pièce jointe",
                ancienneUrl, ev.Id);
            return;
        }

        var uploadsRoot = Path.Combine(_env.ContentRootPath, "uploads");
        var supprimes = ResetCompanyDataCommandHandler.DeleteFiles(uploadsRoot, new[] { ancienneUrl });
        if (supprimes == 0)
            _logger.LogWarning("UploadPdf: ancien PDF {FileUrl} introuvable ou non supprimé (sinistre {AccidentId})", ancienneUrl, ev.Id);
    }

    /// <summary>
    /// Multi-file upload typed by phase — accepts pdf / images / docs.
    /// Payload: <c>file</c> (the file) + <c>documentType</c> (one of
    /// <c>expert_report | mechanic_quote | repair_invoice | insurance_response | police_report | photo | other</c>).
    /// </summary>
    [HttpPost("{id:int}/documents")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<object>> AddDocument(int id, [FromForm] IFormFile? file, [FromForm] string? documentType, CancellationToken ct)
    {
        var (ev, error) = await ValidateUploadAsync(id, file, allowedExt: null, maxSize: 50_000_000, ct);
        if (error != null) return error;

        // Recette du 11/09/2026 (photos des dégâts) : le statut était vérifié par le
        // handler APRÈS l'écriture du fichier — un envoi refusé laissait un fichier
        // orphelin sur le disque (partagé avec la racine du nœud TN). On refuse avant.
        if (ev!.Status is not "confirmed")
            return BadRequest(new { message = $"Impossible d'ajouter un document : l'accident est en statut '{ev.Status}'. Confirmez-le d'abord." });

        // Liste blanche : le type range le document dans une phase de l'écran.
        var type = string.IsNullOrWhiteSpace(documentType) ? "other" : documentType.Trim().ToLowerInvariant();
        if (!AllowedDocumentTypes.Contains(type))
            return BadRequest(new { message = $"Type de document inconnu ({type})." });

        var ext = Path.GetExtension(file!.FileName).ToLowerInvariant();
        var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".doc", ".docx" };
        if (!allowed.Contains(ext))
            return BadRequest(new { message = $"Format non supporté ({ext}). Accepté: PDF, images, Word." });
        if (type == "photo" && !ImageExtensions.Contains(ext))
            return BadRequest(new { message = $"Format non supporté pour une photo ({ext}). Accepté : JPG, PNG, WebP, GIF." });

        // Colonnes bornées (file_name 300, mime_type 100) : un nom trop long faisait
        // échouer l'insertion après l'écriture du fichier.
        var fileName = string.IsNullOrWhiteSpace(file.FileName) ? $"document{ext}" : file.FileName.Trim();
        if (fileName.Length > 300) fileName = fileName[..300];
        var mimeType = file.ContentType;
        if (mimeType is { Length: > 100 }) mimeType = mimeType[..100];

        var (publicUrl, diskPath) = await SaveFileAsync(id, file, ext, ct);
        int docId;
        try
        {
            docId = await _mediator.Send(new AddAccidentDocumentCommand(
                AccidentEventId: id,
                DocumentType: type,
                FileName: fileName,
                FileUrl: publicUrl,
                FileSize: (int)file.Length,
                MimeType: mimeType
            ), ct);
        }
        // Pas de ligne en base → le fichier ne serait jamais référencé : on le retire.
        catch (NotFoundException) { TryDeleteFile(diskPath); return NotFound(); }
        catch (DomainException ex) { TryDeleteFile(diskPath); return BadRequest(new { message = ex.Message }); }
        catch { TryDeleteFile(diskPath); throw; }

        return Ok(new { documentId = docId, fileUrl = publicUrl });
    }

    [HttpDelete("{id:int}/documents/{docId:int}")]
    public async Task<IActionResult> DeleteDocument(int id, int docId, CancellationToken ct)
    {
        // Recette du 11/09/2026 : la suppression retirait la ligne mais laissait le
        // fichier sur le disque. On relève l'URL AVANT la commande (qui porte les
        // contrôles société + statut confirmé) et on n'efface le fichier qu'une fois
        // la ligne supprimée.
        var companyId = _tenantService.CompanyId;
        string? fileUrl = null;
        if (companyId != null)
        {
            fileUrl = await _context.AccidentEventDocuments
                .Where(d => d.Id == docId && d.AccidentEventId == id && d.AccidentEvent!.CompanyId == companyId.Value)
                .Select(d => d.FileUrl)
                .FirstOrDefaultAsync(ct);
        }

        try { await _mediator.Send(new DeleteAccidentDocumentCommand(id, docId), ct); }
        catch (NotFoundException) { return NotFound(); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }

        // Uniquement les fichiers de CET accident, jamais hors de la racine des uploads.
        if (fileUrl != null && fileUrl.StartsWith($"/uploads/accident-reports/{id}/", StringComparison.OrdinalIgnoreCase))
        {
            var uploadsRoot = Path.Combine(_env.ContentRootPath, "uploads");
            var deleted = ResetCompanyDataCommandHandler.DeleteFiles(uploadsRoot, new[] { fileUrl });
            if (deleted == 0)
                _logger.LogWarning("DeleteDocument: fichier {FileUrl} introuvable ou non supprimé (accident {AccidentId})", fileUrl, id);
        }
        return NoContent();
    }

    /// <summary>Types acceptés pour <c>documentType</c> (un par phase de la chronologie).</summary>
    private static readonly HashSet<string> AllowedDocumentTypes = new(StringComparer.Ordinal)
    {
        "photo", "expert_report", "mechanic_quote", "repair_invoice", "insurance_response", "police_report", "other",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.Ordinal)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
    };

    // ── Simulate (debug-only) ─────────────────────────────────────────────

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate(CancellationToken ct)
    {
        var currentUserId = _tenantService.UserId;
        var currentCompanyId = _tenantService.CompanyId;
        if (currentUserId != 1) return Forbid();
        if (currentCompanyId == null) return BadRequest(new { error = "Société non identifiée" });

        var now = DateTime.UtcNow;
        var ev = new AccidentEvent
        {
            CompanyId = currentCompanyId.Value,
            Origin = "auto",
            DeviceUid = "SIMULATE-USER-1",
            IncidentAt = now,
            Latitude = 36.8188,
            Longitude = 10.1657,
            Confidence = 95,
            VehicleLabel = "🧪 Simulation test",
            SynthesisText = "Événement simulé manuellement (bouton de test).",
            Status = "pending",
        };

        _context.AccidentEvents.Add(ev);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AccidentReportsController.Simulate: created simulated accident {AccidentId} for user {UserId} in company {CompanyId}",
            ev.Id, currentUserId.Value, currentCompanyId.Value);

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = ev.Id,
            ["vehicleId"] = 0,
            ["vehicleLabel"] = ev.VehicleLabel!,
            ["incidentAt"] = now.ToString("O"),
            ["latitude"] = ev.Latitude,
            ["longitude"] = ev.Longitude,
            ["magnitude"] = 8000,
            ["speedBeforeKph"] = 65,
            ["requiresDecision"] = "true",
        };

        var localTime = now.ToString("dd/MM/yyyy 'à' HH'h'mm");
        try
        {
            await _notifService.CreateAndSendAsync(
                companyId: currentCompanyId.Value,
                userId: currentUserId.Value,
                type: "accident_detected",
                title: "Accident détecté sur votre véhicule",
                message: $"🧪 SIMULATION — événement test déclenché le {localTime} UTC sur le véhicule {ev.VehicleLabel}.",
                priority: "critical",
                referenceType: "accident_event",
                referenceId: ev.Id,
                actionUrl: $"/rapport-accident/{ev.Id}",
                metadata: metadata,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AccidentReportsController.Simulate: notification fan-out failed for accident {AccidentId}",
                ev.Id);
        }

        return Ok(new { accidentEventId = ev.Id });
    }

    // ── File upload helpers ───────────────────────────────────────────────

    private async Task<(AccidentEvent? Ev, ActionResult? Error)> ValidateUploadAsync(
        int id, IFormFile? file, string? allowedExt, long maxSize, CancellationToken ct)
    {
        // IFormFile? dans les actions (DEF-056) : non nullable, MVC refusait la requête
        // avant l'action avec son message anglais et ce contrôle n'était jamais atteint.
        if (file == null || file.Length == 0)
            return (null, BadRequest(new { message = "Aucun fichier reçu." }));
        if (file.Length > maxSize)
            return (null, BadRequest(new { message = $"Fichier trop volumineux (max {maxSize / 1_000_000} Mo)" }));
        if (allowedExt != null)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != allowedExt) return (null, BadRequest(new { message = $"Format non supporté — uniquement {allowedExt}" }));
        }
        var companyId = _tenantService.CompanyId;
        if (companyId == null) return (null, BadRequest(new { message = "Société non identifiée" }));
        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId.Value, ct);
        if (ev == null) return (null, NotFound());
        return (ev, null);
    }

    /// <param name="prefixe">
    /// Marque du fichier dans son nom (<see cref="PrefixeRapportGenere"/> pour un rapport
    /// produit par l'application) : c'est ce qui distingue plus tard un document fourni
    /// par le client, qu'aucune régénération ne doit effacer.
    /// </param>
    private async Task<(string PublicUrl, string DiskPath)> SaveFileAsync(
        int accidentId, IFormFile file, string ext, CancellationToken ct, string? prefixe = null)
    {
        var uploadsDir = Path.Combine(_env.ContentRootPath, "uploads", "accident-reports", accidentId.ToString());
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);
        var uniqueName = $"{prefixe}{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, uniqueName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream, ct);
        }
        return ($"/uploads/accident-reports/{accidentId}/{uniqueName}", filePath);
    }

    /// <summary>Retire un fichier que l'on vient d'écrire (chemin produit par SaveFileAsync).</summary>
    private void TryDeleteFile(string diskPath)
    {
        try { if (System.IO.File.Exists(diskPath)) System.IO.File.Delete(diskPath); }
        catch (Exception ex) { _logger.LogWarning(ex, "AccidentReportsController: fichier orphelin non supprimé {Path}", diskPath); }
    }
}

// ── Request DTOs ────────────────────────────────────────────────────────────

public record CreateManualAccidentRequest(
    int VehicleId,
    DateTime IncidentAt,
    double? Latitude,
    double? Longitude,
    string? LocationCommune,
    string? LocationGovernorate,
    string? Description,
    string? Severity,
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes);

public record UpdateInitialDamagesRequest(
    string? Description,
    string? Severity,
    List<string>? DamagedZones,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? WeatherConditions,
    string? RoadConditions);

public record RegisterExpertAssessmentRequest(
    DateTime? VisitedAt,
    string? ExpertName,
    string? ExpertCompany,
    string? Assessment,
    decimal? EstimatedAmount);

public record RegisterMechanicQuoteRequest(
    DateTime? QuoteAt,
    string? MechanicName,
    decimal? QuotedAmount);

public record RegisterRepairRequest(
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualCost);

public record RegisterClaimRequest(
    string? ClaimNumber,
    DateTime? SubmittedAt,
    decimal? ApprovedAmount,
    string? Status,
    bool? ThirdPartyInvolved);

public record AddThirdPartyRequest(
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber,
    DateTime? InsuranceExpiry);
