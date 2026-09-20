using System.Globalization;
using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Retour des phases qui alimentent Réparations (phase 5) ou Dépenses (phase 6).
///
/// Ces phases répondaient 204 quoi qu'il arrive : quand le véhicule du sinistre
/// avait été supprimé, la réparation ou le remboursement n'était pas écrit et
/// PERSONNE ne le savait (recette Karim du 18/09/2026). La phase s'enregistre
/// toujours — refuser aurait poussé à vider un montant déjà saisi — mais elle dit
/// désormais ce qu'elle n'a pas pu faire.
/// </summary>
/// <param name="Synced">La ligne liée (réparation ou dépense) est à jour.</param>
/// <param name="Warning">Message destiné à l'écran, null si tout s'est bien passé.</param>
/// <param name="Reason">
/// Code de la cause : <c>vehicle_deleted</c> | <c>no_vehicle</c> (rien n'a pu être
/// reporté) ou <c>manual_edits</c> (la phase a laissé en place une ligne enrichie à la
/// main, la phase elle-même s'est enregistrée).
/// </param>
public record PhaseSyncResult(bool Synced, string? Warning = null, string? Reason = null)
{
    public static PhaseSyncResult Ok { get; } = new(true);
}

// =============================================================================
// Calypso 7 — phase-specific commands for the unified accident timeline.
//
// All commands share the same shape:
//   - tenant-scoped lookup by id + companyId
//   - guard against editing dismissed/pending rows (must be confirmed first)
//   - idempotent: re-submit replaces / merges values
//   - stamp UpdatedAt and SaveChangesAsync
//
// Phase 5 (RegisterRepair) reporte le coût réel dans l'écran RÉPARATIONS
// (table repairs, migration 049) et Phase 6 (RegisterClaim) reporte le
// remboursement dans DÉPENSES (vehicle_costs). Les deux reports sont
// symétriques : un montant corrigé met la ligne à jour, un montant vidé la
// retire, et un véhicule supprimé rend un avertissement (PhaseSyncResult).
// =============================================================================

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 2 — Initial damages capture (post-confirmation)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 2: initial visible damages captured by the admin
/// just after confirming the accident. No cost yet (it is unknown on
/// day 0). Description, severity, list of damaged zones.
/// </summary>
public record UpdateInitialDamagesCommand(
    int AccidentEventId,
    string? Description,
    string? Severity,
    List<string>? DamagedZones,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? WeatherConditions,
    string? RoadConditions
) : IRequest<Unit>;

public class UpdateInitialDamagesCommandHandler : PhaseCommandHandlerBase, IRequestHandler<UpdateInitialDamagesCommand, Unit>
{
    public UpdateInitialDamagesCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<UpdateInitialDamagesCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(UpdateInitialDamagesCommand request, CancellationToken ct)
    {
        // Les zones sont des LIBELLÉS (« arriere », « coffre »), jamais des chemins : une
        // chaîne libre « /uploads/…/../… » était enregistrée telle quelle, puis relue comme
        // un fichier à effacer à la suppression du dossier. Refus avant toute écriture.
        if (request.DamagedZones?.Any(z => z is not null
                && (z.Contains('/') || z.Contains('\\') || z.Contains(".."))) == true)
            throw new DomainException(
                "Zone endommagée invalide : une zone est un libellé (par exemple « arrière » ou "
                + "« coffre »), sans « / », « \\ » ni « .. ». Aucune modification n'a été enregistrée.");

        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.InitialDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ev.InitialSeverity = NormaliseSeverity(request.Severity);
        ev.DamagedZonesJson = request.DamagedZones?.Count > 0
            ? JsonSerializer.Serialize(request.DamagedZones)
            : null;
        ev.PoliceReportNumber = string.IsNullOrWhiteSpace(request.PoliceReportNumber) ? null : request.PoliceReportNumber.Trim();
        ev.MileageAtAccident = request.MileageAtAccident;
        ev.WeatherConditions = string.IsNullOrWhiteSpace(request.WeatherConditions) ? null : request.WeatherConditions.Trim();
        ev.RoadConditions = string.IsNullOrWhiteSpace(request.RoadConditions) ? null : request.RoadConditions.Trim();
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 3 — Expert assessment (insurance expert visit)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 3: insurance expert visited the vehicle and gave
/// a written assessment + estimated repair amount. May happen days or
/// weeks after the accident.
/// </summary>
public record RegisterExpertAssessmentCommand(
    int AccidentEventId,
    DateTime? VisitedAt,
    string? ExpertName,
    string? ExpertCompany,
    string? Assessment,
    decimal? EstimatedAmount
) : IRequest<Unit>;

public class RegisterExpertAssessmentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterExpertAssessmentCommand, Unit>
{
    public RegisterExpertAssessmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterExpertAssessmentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterExpertAssessmentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.ExpertVisitedAt = request.VisitedAt?.ToUniversalTime();
        ev.ExpertName = string.IsNullOrWhiteSpace(request.ExpertName) ? null : request.ExpertName.Trim();
        ev.ExpertCompany = string.IsNullOrWhiteSpace(request.ExpertCompany) ? null : request.ExpertCompany.Trim();
        ev.ExpertAssessment = string.IsNullOrWhiteSpace(request.Assessment) ? null : request.Assessment.Trim();
        ev.ExpertEstimatedAmount = request.EstimatedAmount;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 4 — Mechanic quote
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 4: garage / mechanic provided a repair quote.
/// </summary>
public record RegisterMechanicQuoteCommand(
    int AccidentEventId,
    DateTime? QuoteAt,
    string? MechanicName,
    decimal? QuotedAmount
) : IRequest<Unit>;

public class RegisterMechanicQuoteCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterMechanicQuoteCommand, Unit>
{
    public RegisterMechanicQuoteCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterMechanicQuoteCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterMechanicQuoteCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.MechanicQuoteAt = request.QuoteAt?.ToUniversalTime();
        ev.MechanicName = string.IsNullOrWhiteSpace(request.MechanicName) ? null : request.MechanicName.Trim();
        ev.MechanicQuotedAmount = request.QuotedAmount;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 5 — Repair (auto-syncs VehicleCost)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 5 : suivi de la réparation, reportée dans l'écran RÉPARATIONS.
///
/// Le coût réel créait une dépense <c>vehicle_costs</c> de type « repair » : le
/// client cherchait sa réparation dans l'écran Réparations, où elle n'a jamais
/// figuré (recette Karim du 18/09/2026). La phase écrit donc une ligne
/// <c>repairs</c> rattachée au dossier (migration 049), mise à jour à chaque
/// enregistrement, et retirée si le coût réel est vidé ou remis à zéro.
///
/// Le montant reste compté UNE fois : l'agrégateur des coûts additionne les
/// lignes de <c>repairs</c> ET les dépenses de catégorie Réparation, la dépense
/// « repair » du dossier est donc retirée au moment où la ligne de réparation est
/// écrite.
/// </summary>
public record RegisterRepairCommand(
    int AccidentEventId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualCost
) : IRequest<PhaseSyncResult>;

public class RegisterRepairCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterRepairCommand, PhaseSyncResult>
{
    public RegisterRepairCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterRepairCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<PhaseSyncResult> Handle(RegisterRepairCommand request, CancellationToken ct)
    {
        // Montant contrôlé AVANT toute écriture : repairs.total_cost est un
        // decimal(10,2), au-delà l'enregistrement échouait en 500.
        EnsureAmountFits(request.ActualCost, "Coût réel de la réparation");

        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        // Date que la phase avait posée sur la réparation, relevée AVANT d'écrire les
        // nouvelles dates : c'est elle qui dit si la date de la ligne appartient encore
        // à la phase ou si elle a été corrigée dans l'écran Réparations.
        var dateDejaPosee = DateDeLaPhase(ev);
        ev.RepairStartedAt = request.StartedAt?.ToUniversalTime();
        ev.RepairCompletedAt = request.CompletedAt?.ToUniversalTime();
        ev.ActualRepairCost = request.ActualCost;
        ev.UpdatedAt = DateTime.UtcNow;

        // Ce que la phase n'a pas pu faire à la place de l'utilisateur, remonté à l'écran
        // par PhaseSyncResult : elle ne détruit que ce qu'elle a posé elle-même.
        var avertissements = new List<string>();

        var result = PhaseSyncResult.Ok;
        if (request.ActualCost is > 0)
        {
            result = await EnsureVehicleAsync(ev, "la réparation", ct);
            if (result.Synced)
            {
                avertissements.AddRange(
                    await UpsertAccidentRepairAsync(ev, request.ActualCost.Value, dateDejaPosee, ct));
                // La dépense « repair » d'un dossier antérieur laisse la place à la ligne
                // de réparation : sans ce retrait le même montant serait compté deux fois.
                // APRÈS l'écriture de la réparation : retirée d'abord, elle disparaissait
                // des coûts sans que rien ne la remplace.
                avertissements.AddRange(await RetirerDepenseDeReparationAsync(ev, ct));
            }
        }
        else
        {
            // Montant vidé, à zéro ou négatif : la réparation n'a plus lieu d'être, et la
            // dépense « repair » d'un dossier antérieur non plus.
            avertissements.AddRange(await RemoveAccidentRepairAsync(ev, ct));
            avertissements.AddRange(await RetirerDepenseDeReparationAsync(ev, ct));
        }

        await Context.SaveChangesAsync(ct);
        if (avertissements.Count == 0) return result;
        // Messages CONCATÉNÉS, jamais écartés : celui du véhicule vient d'abord (rien n'a
        // pu être reporté), ceux de la saisie ensuite (une ligne conservée reste à
        // arbitrer). Le second disparaissait dès que le premier existait.
        var messages = result.Warning is { Length: > 0 } premier
            ? new[] { premier }.Concat(avertissements)
            : avertissements;
        return result with
        {
            Warning = string.Join(" ", messages),
            Reason = result.Reason ?? "manual_edits",
        };
    }

    /// <summary>
    /// Crée ou met à jour LA ligne de réparation du dossier. Retrouvée par
    /// <c>accident_event_id</c> : une seconde sauvegarde de la phase corrige la ligne
    /// au lieu d'en empiler une nouvelle.
    ///
    /// <para>À la création la phase renseigne tout. À la mise à jour elle ne reprend
    /// que ce qui lui appartient encore (description, date, note, statut) : la ligne
    /// vit aussi dans l'écran Réparations, où l'utilisateur la précise.</para>
    /// </summary>
    /// <param name="dateDejaPosee">Date que la phase avait posée avant cet enregistrement.</param>
    /// <returns>Avertissements destinés à l'écran (liste vide si tout s'est bien passé).</returns>
    private async Task<List<string>> UpsertAccidentRepairAsync(AccidentEvent ev, decimal amount, DateTime dateDejaPosee, CancellationToken ct)
    {
        var avertissements = new List<string>();
        var repair = await Context.Repairs
            .FirstOrDefaultAsync(r => r.AccidentEventId == ev.Id && r.SocieteId == ev.CompanyId, ct);

        var now = DateTime.UtcNow;
        var creation = repair == null;
        if (repair == null)
        {
            repair = new Repair
            {
                SocieteId = ev.CompanyId,
                AccidentEventId = ev.Id,
                // Même numérotation que l'écran Réparations : la facture d'un sinistre
                // porte une référence du même format que les autres.
                Reference = await CreateRepairCommandHandler.NextReferenceAsync(Context, ev.CompanyId, now, ct),
                CreatedAt = now,
                CreatedBy = TenantService.UserId,
            };
            Context.Repairs.Add(repair);
            Logger.LogInformation(
                "Phase 5 : réparation {Reference} créée pour le sinistre {Accident} (montant {Amount})",
                repair.Reference, ev.Id, amount);
        }
        else
        {
            repair.UpdatedAt = now;
        }

        repair.VehicleId = ev.VehicleId!.Value;
        // Même règle que pour la note : la description n'est reprise que si elle est
        // vide ou encore celle posée par la phase. « Remplacement pare-chocs avant +
        // peinture » saisi dans Réparations était sinon effacé au réenregistrement.
        if (creation || EstDescriptionDeLaPhase(repair.Description))
            repair.Description = Truncate($"{PrefixeDescription}{ReferenceLabel(ev)}", 500);
        // Date de FIN de réparation : c'est le mois où la facture tombe. À défaut,
        // le début, puis la date de l'accident — jamais le jour de la saisie, qui
        // déplacerait la réparation d'un mois à l'autre à chaque enregistrement.
        // Une date corrigée sur la facture dans l'écran Réparations ne correspond plus
        // à celle que la phase avait posée : elle reste.
        if (creation || repair.RepairDate.Date == dateDejaPosee.Date)
            repair.RepairDate = DateTime.SpecifyKind(DateDeLaPhase(ev), DateTimeKind.Utc);
        // Les pièces saisies à la main dans l'écran Réparations survivent à une seconde
        // sauvegarde de la phase : les nier remettait parts_cost à 0 alors que les lignes
        // restaient affichées avec leurs sous-totaux, et la fiche se contredisait.
        // Le coût réel facturé du sinistre reste la vérité : la main-d'œuvre est ce qu'il
        // reste une fois les pièces déduites.
        var partsCost = repair.Id == 0
            ? 0m
            : await Context.RepairParts
                .Where(p => p.RepairId == repair.Id)
                .SumAsync(p => (decimal?)p.Subtotal, ct) ?? 0m;
        // total = main-d'œuvre + pièces est l'invariant de l'écran Réparations
        // (CreateRepair/UpdateRepair calculent le total ainsi) : les pièces sont donc
        // bornées par le coût facturé plutôt que de casser l'addition affichée.
        if (partsCost > amount)
        {
            Logger.LogWarning(
                "Sinistre {Accident} : pièces saisies ({Parts}) supérieures au coût réel ({Amount}), main-d'œuvre ramenée à 0",
                ev.Id, partsCost, amount);
            // L'écran affichait des lignes de pièces dont la somme ne correspondait plus
            // au sous-total « pièces » de l'en-tête, sans un mot : seul le journal le
            // disait, et personne ne le lit.
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            avertissements.Add(
                $"Les pièces saisies dans Réparations ({partsCost.ToString("N2", fr)}) dépassent le coût réel " +
                $"({amount.ToString("N2", fr)}) : la main-d'œuvre a été ramenée à 0.");
        }
        repair.PartsCost = Math.Min(partsCost, amount);
        repair.LaborCost = amount - repair.PartsCost;
        repair.TotalCost = amount;
        // Terminée seulement si la date de fin est renseignée : sinon le véhicule est
        // encore à l'atelier, et « Terminée » ferait mentir le compteur de l'écran.
        // Un statut CHOISI dans l'écran Réparations (« annulée », « en attente ») est
        // conservé : la phase ressuscitait une réparation annulée en « Terminée » et son
        // montant revenait dans les coûts.
        if (creation || EstStatutDeLaPhase(repair.Status))
            repair.Status = ev.RepairCompletedAt.HasValue ? RepairInputRules.Completed : RepairInputRules.InProgress;
        // Le garage du dossier est un nom libre (phase 4), pas un fournisseur de la
        // base : il va dans les notes, supplier_id resterait faux — et comme la phase
        // n'écrit jamais supplier_id, un fournisseur choisi à l'écran survit.
        // La note n'est réécrite que si elle est vide ou si elle est encore celle posée
        // par la phase : une note saisie dans Réparations (pièces d'occasion, garantie,
        // immobilisation) était sinon effacée à chaque réenregistrement de la phase.
        if (creation || EstNoteDeLaPhase(repair.Notes))
            repair.Notes = string.IsNullOrWhiteSpace(ev.MechanicName)
                ? null
                : Truncate($"{PrefixeNoteGarage}{ev.MechanicName.Trim()}", 1000);

        return avertissements;
    }

    /// <summary>Début de la note posée par la phase 5 à partir du garage de la phase 4.</summary>
    private const string PrefixeNoteGarage = "Garage : ";

    /// <summary>Début de la description posée par la phase 5.</summary>
    private const string PrefixeDescription = "Réparation accident — ";

    /// <summary>
    /// La note appartient-elle encore à la phase ? Vide, ou de la forme exacte qu'elle
    /// écrit (« Garage : … » sur une seule ligne). Tout le reste a été saisi dans
    /// l'écran Réparations et ne doit jamais être écrasé.
    /// </summary>
    private static bool EstNoteDeLaPhase(string? notes) =>
        string.IsNullOrWhiteSpace(notes)
        || (notes.StartsWith(PrefixeNoteGarage, StringComparison.Ordinal)
            && notes.IndexOfAny(new[] { '\r', '\n' }) < 0);

    /// <summary>
    /// La description appartient-elle encore à la phase ? Vide, ou de la forme qu'elle
    /// écrit (« Réparation accident — … » sur une seule ligne).
    /// </summary>
    private static bool EstDescriptionDeLaPhase(string? description) =>
        string.IsNullOrWhiteSpace(description)
        || (description.StartsWith(PrefixeDescription, StringComparison.Ordinal)
            && description.IndexOfAny(new[] { '\r', '\n' }) < 0);

    /// <summary>
    /// Le statut est-il encore celui que la phase avait écrit ? Seuls « en cours » et
    /// « terminée » sont les siens (vide compris, pour les lignes anciennes) : « annulée »
    /// et « en attente » viennent de l'écran Réparations et lui appartiennent.
    /// </summary>
    private static bool EstStatutDeLaPhase(string? status) =>
        string.IsNullOrWhiteSpace(status)
        || RepairInputRules.HasStatus(status, RepairInputRules.InProgress)
        || RepairInputRules.HasStatus(status, RepairInputRules.Completed);

    /// <summary>
    /// Date que la phase pose sur la réparation : fin de réparation, à défaut début,
    /// à défaut jour de l'accident.
    /// </summary>
    private static DateTime DateDeLaPhase(AccidentEvent ev) =>
        ev.RepairCompletedAt ?? ev.RepairStartedAt ?? ev.IncidentAt;

    /// <summary>
    /// Retire la ligne de réparation du dossier quand le coût réel est effacé — mais
    /// seulement si elle est ENTIÈREMENT celle de la phase. La ligne vit aussi dans
    /// l'écran Réparations : dès qu'elle porte des pièces, un fournisseur, un numéro de
    /// facture, une description, une note ou un statut qui ne sont plus ceux de la phase,
    /// elle est DÉTACHÉE et l'écran le dit. Même règle qu'à la mise à jour : la phase
    /// n'écrase et ne détruit jamais ce que l'utilisateur a saisi.
    /// </summary>
    /// <returns>Avertissements destinés à l'écran (liste vide si la ligne a été retirée).</returns>
    private async Task<List<string>> RemoveAccidentRepairAsync(AccidentEvent ev, CancellationToken ct)
    {
        var avertissements = new List<string>();
        var repair = await Context.Repairs
            .FirstOrDefaultAsync(r => r.AccidentEventId == ev.Id && r.SocieteId == ev.CompanyId, ct);
        if (repair == null) return avertissements;

        var parts = await Context.RepairParts.Where(p => p.RepairId == repair.Id).ToListAsync(ct);
        var toutDeLaPhase = parts.Count == 0
            && repair.SupplierId == null
            && string.IsNullOrWhiteSpace(repair.InvoiceNumber)
            && EstDescriptionDeLaPhase(repair.Description)
            && EstNoteDeLaPhase(repair.Notes)
            && EstStatutDeLaPhase(repair.Status);

        if (!toutDeLaPhase)
        {
            repair.AccidentEventId = null;
            repair.UpdatedAt = DateTime.UtcNow;
            Logger.LogWarning(
                "Phase 5 : réparation {Reference} conservée et détachée du sinistre {Accident} (saisie propre à l'écran Réparations)",
                repair.Reference, ev.Id);
            avertissements.Add(
                "Le coût a été retiré du dossier ; la réparation saisie dans Réparations a été conservée, détachée.");
            return avertissements;
        }

        Context.Repairs.Remove(repair);
        Logger.LogInformation(
            "Phase 5 : réparation {Reference} retirée (coût réel effacé sur le sinistre {Accident})",
            repair.Reference, ev.Id);
        return avertissements;
    }

    /// <summary>
    /// Retire la dépense « repair » d'un dossier antérieur à la migration 049, qui serait
    /// sinon comptée en plus de la ligne de réparation. Même garde que ci-dessus : une
    /// dépense reprise dans l'écran Dépenses (justificatif scanné, numéro de pièce,
    /// détail de facture, fournisseur, note) est DÉTACHÉE, jamais supprimée — le fichier
    /// du justificatif serait resté orphelin sur le disque.
    /// </summary>
    /// <returns>Avertissements destinés à l'écran (liste vide si tout a été retiré).</returns>
    private async Task<List<string>> RetirerDepenseDeReparationAsync(AccidentEvent ev, CancellationToken ct)
    {
        var avertissements = new List<string>();
        // Chargées sur le seul lien au dossier — une poignée de lignes — puis classées
        // avec la règle des rapports. Le filtre exact « repair » laissait passer une
        // dépense retypée « Réparation » ou « Réparation accident » dans l'écran
        // Dépenses : elle restait EN PLUS de la ligne de réparation, et son montant
        // comptait deux fois dans les coûts sans que rien ne le dise.
        var depenses = (await Context.VehicleCosts
                .Where(c => c.AccidentEventId == ev.Id)
                .ToListAsync(ct))
            .Where(c => VehicleCostCategory.IsRepair(c.Type))
            .ToList();
        if (depenses.Count == 0) return avertissements;

        var aSupprimer = depenses.Where(EstDepenseDeLaPhase).ToList();
        var aDetacher = depenses.Where(c => !EstDepenseDeLaPhase(c)).ToList();

        if (aSupprimer.Count > 0)
        {
            Context.VehicleCosts.RemoveRange(aSupprimer);
            Logger.LogInformation(
                "Sinistre {Accident} : {Count} dépense(s) de réparation retirée(s) au profit de la ligne de réparation",
                ev.Id, aSupprimer.Count);
        }

        foreach (var depense in aDetacher) depense.AccidentEventId = null;
        if (aDetacher.Count > 0)
        {
            Logger.LogWarning(
                "Sinistre {Accident} : {Count} dépense(s) de réparation conservée(s) et détachée(s) (saisie propre à l'écran Dépenses)",
                ev.Id, aDetacher.Count);
            // Énumération alignée sur EstDepenseDeLaPhase — une simple note suffit à
            // conserver la ligne — et conséquence nommée : le doublon compte double
            // jusqu'à ce que l'utilisateur tranche.
            avertissements.Add(
                "Une dépense de réparation de ce dossier a été reprise à la main (justificatif, fournisseur, " +
                "numéro de pièce, détail de facture ou note) : elle reste dans Dépenses, détachée du sinistre. " +
                "Le montant est compté deux fois tant que l'une des deux lignes n'est pas supprimée.");
        }
        return avertissements;
    }

    /// <summary>
    /// La dépense a-t-elle été posée par la phase, et rien d'autre ? La phase n'écrit ni
    /// justificatif, ni numéro de pièce, ni détail de facture, ni fournisseur, ni note :
    /// l'un d'eux renseigné, la ligne a été reprise dans l'écran Dépenses.
    /// </summary>
    private static bool EstDepenseDeLaPhase(VehicleCost cout) =>
        string.IsNullOrWhiteSpace(cout.ReceiptUrl)
        && string.IsNullOrWhiteSpace(cout.ReceiptNumber)
        && string.IsNullOrWhiteSpace(cout.DetailsJson)
        && string.IsNullOrWhiteSpace(cout.Provider)
        && string.IsNullOrWhiteSpace(cout.Notes);
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 6 — Sinistre assurance (dépense de remboursement)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 6 : suivi du sinistre assurance. Le montant approuvé est
/// reporté dans Dépenses en ligne <c>insurance_refund</c> — créée, mise à jour ou
/// RETIRÉE selon le montant, symétriquement à la réparation de la phase 5.
/// </summary>
public record RegisterClaimCommand(
    int AccidentEventId,
    string? ClaimNumber,
    DateTime? SubmittedAt,
    decimal? ApprovedAmount,
    string? Status,                  // pending|approved|partial|rejected|closed
    bool? ThirdPartyInvolved
) : IRequest<PhaseSyncResult>;

public class RegisterClaimCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterClaimCommand, PhaseSyncResult>
{
    public RegisterClaimCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterClaimCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<PhaseSyncResult> Handle(RegisterClaimCommand request, CancellationToken ct)
    {
        // Contrôle AVANT tout chargement ou modification : la phase 6 est réécrite en
        // bloc, et un statut hors liste devenait NULL en silence — l'appel répondait
        // 204 et le suivi assurance déjà saisi était perdu (recette GPA, DEF-021).
        var claimStatus = NormaliseClaimStatus(request.Status);
        EnsureAmountFits(request.ApprovedAmount, "Montant approuvé");

        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.ClaimNumber = string.IsNullOrWhiteSpace(request.ClaimNumber) ? null : request.ClaimNumber.Trim();
        ev.ClaimSubmittedAt = request.SubmittedAt?.ToUniversalTime();
        ev.ClaimApprovedAmount = request.ApprovedAmount;
        ev.ClaimStatus = claimStatus;
        if (request.ThirdPartyInvolved.HasValue) ev.ThirdPartyInvolved = request.ThirdPartyInvolved.Value;
        ev.UpdatedAt = DateTime.UtcNow;

        // Remboursement enregistré en montant POSITIF, affiché en crédit par l’écran
        // Dépenses. Un montant corrigé met la ligne à jour ; un montant vidé, remis à
        // zéro ou négatif la retire — sinon un remboursement saisi par erreur restait
        // définitivement dans Dépenses (recette Karim du 18/09/2026). Sinistre REJETÉ :
        // pas de crédit non plus, quel que soit le montant resté dans le formulaire.
        var result = PhaseSyncResult.Ok;
        if (request.ApprovedAmount is > 0 && claimStatus != "rejected")
        {
            result = await EnsureVehicleAsync(ev, "le remboursement d'assurance", ct);
            if (result.Synced)
            {
                await UpsertVehicleCostAsync(
                    ev,
                    type: "insurance_refund",
                    amount: request.ApprovedAmount.Value,
                    date: ev.ClaimSubmittedAt,
                    description: $"Remboursement assurance — {ev.ClaimNumber ?? ReferenceLabel(ev)}",
                    ct: ct);
            }
        }
        else
        {
            // Montant approuvé vidé, remis à zéro, négatif ou sinistre rejeté : le
            // remboursement enregistré avant n'existe plus. Le crédit allège tous les
            // totaux de coûts (tableau de bord, rapports, IA) : laissé en base, il les
            // minorait sans contrepartie.
            await RemoveVehicleCostAsync(ev, "insurance_refund", ct);
        }

        await Context.SaveChangesAsync(ct);
        return result;
    }

    private static readonly string[] ClaimStatuses = { "pending", "approved", "partial", "rejected", "closed" };

    /// <summary>
    /// Vide = pas de statut (NULL). Valeur inconnue = refus explicite (400), jamais NULL.
    /// </summary>
    private static string? NormaliseClaimStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lc = raw.Trim().ToLowerInvariant();
        if (ClaimStatuses.Contains(lc)) return lc;
        throw new DomainException(
            $"Statut de sinistre inconnu : « {raw.Trim()} ». Valeurs acceptées : " +
            "pending (en cours), approved (approuvé), partial (partiellement approuvé), rejected (rejeté), closed (clos). " +
            "Aucune modification n'a été enregistrée.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Third parties (other vehicles / parties involved)
// ─────────────────────────────────────────────────────────────────────────────

public record AddThirdPartyCommand(
    int AccidentEventId,
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber,
    DateTime? InsuranceExpiry
) : IRequest<int>;

public class AddThirdPartyCommandHandler : PhaseCommandHandlerBase, IRequestHandler<AddThirdPartyCommand, int>
{
    public AddThirdPartyCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<AddThirdPartyCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<int> Handle(AddThirdPartyCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var tp = new AccidentEventThirdParty
        {
            AccidentEventId = ev.Id,
            Name = request.Name?.Trim(),
            Phone = request.Phone?.Trim(),
            VehiclePlate = request.VehiclePlate?.Trim(),
            VehicleModel = request.VehicleModel?.Trim(),
            InsuranceCompany = request.InsuranceCompany?.Trim(),
            InsuranceNumber = request.InsuranceNumber?.Trim(),
            InsuranceExpiry = request.InsuranceExpiry,
        };
        Context.AccidentEventThirdParties.Add(tp);
        ev.ThirdPartyInvolved = true;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return tp.Id;
    }
}

public record DeleteThirdPartyCommand(int AccidentEventId, int ThirdPartyId) : IRequest<Unit>;

public class DeleteThirdPartyCommandHandler : PhaseCommandHandlerBase, IRequestHandler<DeleteThirdPartyCommand, Unit>
{
    public DeleteThirdPartyCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<DeleteThirdPartyCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(DeleteThirdPartyCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var tp = await Context.AccidentEventThirdParties
            .FirstOrDefaultAsync(t => t.Id == request.ThirdPartyId && t.AccidentEventId == ev.Id, ct);
        if (tp == null) return Unit.Value; // idempotent
        Context.AccidentEventThirdParties.Remove(tp);
        // Le dossier a changé : sans cette date, le PDF déjà produit gardait le tiers
        // retiré et l'indicateur « PDF antérieur » ne s'allumait pas (l'ajout, lui,
        // la posait déjà) — le document remis à l'assureur nommait un tiers effacé.
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Documents (multi-file attachments typed by phase)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — registers an already-uploaded file URL on the accident.
/// The controller takes care of writing the file to disk; this handler
/// only persists the metadata so the file is listed under the right
/// phase tab in the timeline UI.
/// </summary>
public record AddAccidentDocumentCommand(
    int AccidentEventId,
    string DocumentType,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? MimeType
) : IRequest<int>;

public class AddAccidentDocumentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<AddAccidentDocumentCommand, int>
{
    public AddAccidentDocumentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<AddAccidentDocumentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<int> Handle(AddAccidentDocumentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var doc = new AccidentEventDocument
        {
            AccidentEventId = ev.Id,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "other" : request.DocumentType.Trim(),
            FileName = request.FileName,
            FileUrl = request.FileUrl,
            FileSize = request.FileSize,
            MimeType = request.MimeType,
            UploadedByUserId = TenantService.UserId,
            UploadedAt = DateTime.UtcNow,
        };
        Context.AccidentEventDocuments.Add(doc);
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return doc.Id;
    }
}

public record DeleteAccidentDocumentCommand(int AccidentEventId, int DocumentId) : IRequest<Unit>;

public class DeleteAccidentDocumentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<DeleteAccidentDocumentCommand, Unit>
{
    public DeleteAccidentDocumentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<DeleteAccidentDocumentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(DeleteAccidentDocumentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var doc = await Context.AccidentEventDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.AccidentEventId == ev.Id, ct);
        if (doc == null) return Unit.Value;
        Context.AccidentEventDocuments.Remove(doc);
        // Même raison que pour un tiers retiré : la pièce jointe disparaît de la fiche,
        // elle doit disparaître du PDF — donc marquer le dossier comme modifié.
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// =============================================================================
//  Shared base class — tenant-aware loading + VehicleCost upsert
// =============================================================================

public abstract class PhaseCommandHandlerBase
{
    protected IGisDbContext Context { get; }
    protected ICurrentTenantService TenantService { get; }
    protected ILogger Logger { get; }

    protected PhaseCommandHandlerBase(IGisDbContext context, ICurrentTenantService tenantService, ILogger logger)
    {
        Context = context;
        TenantService = tenantService;
        Logger = logger;
    }

    /// <summary>
    /// Loads an accident row scoped to the caller's tenant and asserts
    /// it is in <c>"confirmed"</c> status. Pending / dismissed rows
    /// reject — phase data only makes sense after confirmation.
    /// </summary>
    protected async Task<AccidentEvent> LoadConfirmedAsync(int accidentEventId, CancellationToken ct)
    {
        var companyId = TenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var ev = await Context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == accidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", accidentEventId);
        if (ev.Status is not "confirmed")
            throw new DomainException(
                $"Impossible de modifier les phases: l'accident est en statut '{ev.Status}'. Confirmez-le d'abord.");
        return ev;
    }

    /// <summary>Référence lisible du dossier, citée dans les libellés (« ACC-2026-014 » ou « #42 »).</summary>
    protected static string ReferenceLabel(AccidentEvent ev) =>
        ev.ReferenceCode is { Length: > 0 } code ? code : $"#{ev.Id}";

    protected static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    /// <summary>
    /// Montants bornés par la capacité des colonnes (<c>decimal(10,2)</c> sur
    /// <c>repairs.total_cost</c> comme sur <c>vehicle_costs.amount</c>) : au-delà
    /// l'enregistrement partait en 500 sans rien dire. Refus explicite, même
    /// formulation que l'écran Réparations.
    /// </summary>
    protected static void EnsureAmountFits(decimal? amount, string champ)
    {
        if (amount is null || amount <= RepairInputRules.MaxAmount) return;
        var plafond = RepairInputRules.MaxAmount.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
        throw new DomainException($"{champ} : le montant ne peut pas dépasser {plafond}.");
    }

    /// <summary>
    /// Vérifie que le véhicule du dossier existe encore AVANT d'écrire la ligne qui en
    /// dépend. <c>accident_events.vehicle_id</c> n'a pas de clé étrangère : un sinistre
    /// survit à la suppression de son véhicule, pas ses dépenses ni ses réparations.
    /// La phase et son montant s'enregistrent quand même — refuser aurait poussé à
    /// vider un coût déjà saisi — mais l'appelant reçoit de quoi le DIRE à l'écran.
    /// </summary>
    protected async Task<PhaseSyncResult> EnsureVehicleAsync(AccidentEvent ev, string quoi, CancellationToken ct)
    {
        if (!ev.VehicleId.HasValue)
        {
            // La suppression d'un véhicule fige d'abord vehicle_label sur ses dossiers,
            // puis met vehicle_id à NULL (VehicleDeletionHelper) : un libellé sans
            // identifiant dit « véhicule supprimé », pas « jamais rattaché ». C'est le
            // cas RÉEL depuis DEF-046 — un vehicle_id pendant n'est plus produit.
            var supprime = !string.IsNullOrWhiteSpace(ev.VehicleLabel);
            return new PhaseSyncResult(false,
                supprime
                    ? $"Montant enregistré, mais {quoi} n'a pas pu être reportée : le véhicule de ce dossier a été supprimé."
                    : $"Montant enregistré, mais {quoi} n'a pas pu être reportée : ce dossier n'est rattaché à aucun véhicule.",
                supprime ? "vehicle_deleted" : "no_vehicle");
        }

        var vehicleExists = await Context.Vehicles
            .AnyAsync(v => v.Id == ev.VehicleId.Value && v.CompanyId == ev.CompanyId, ct);
        if (vehicleExists) return PhaseSyncResult.Ok;

        Logger.LogWarning(
            "Sinistre {Accident} : véhicule {Vehicle} supprimé, {Quoi} non reportée",
            ev.Id, ev.VehicleId.Value, quoi);
        return new PhaseSyncResult(false,
            $"Montant enregistré, mais {quoi} n'a pas pu être reportée : le véhicule de ce dossier a été supprimé.",
            "vehicle_deleted");
    }

    /// <summary>
    /// Retire la dépense liée au dossier pour ce type, quand le montant est vidé : un
    /// remboursement saisi par erreur doit disparaître de Dépenses. La phase 5 a sa
    /// propre variante, qui détache au lieu de détruire une dépense reprise à la main
    /// (<c>RetirerDepenseDeReparationAsync</c>).
    /// </summary>
    protected async Task RemoveVehicleCostAsync(AccidentEvent ev, string type, CancellationToken ct)
    {
        var existing = await Context.VehicleCosts
            .Where(c => c.AccidentEventId == ev.Id && c.Type == type)
            .ToListAsync(ct);
        if (existing.Count == 0) return;

        Context.VehicleCosts.RemoveRange(existing);
        Logger.LogInformation(
            "Sinistre {Accident} : {Count} dépense(s) de type {Type} retirée(s)",
            ev.Id, existing.Count, type);
    }

    /// <summary>
    /// Idempotent upsert of a <see cref="VehicleCost"/> row linked to
    /// an accident. Looks up an existing row by
    /// <c>(VehicleId, AccidentEventId, Type)</c> — if present, updates
    /// amount/date/description; otherwise inserts a new one.
    /// </summary>
    /// <param name="date">
    /// Date métier (réparation terminée, dépôt du sinistre). Absente : une ligne
    /// existante garde sa date, une nouvelle ligne prend la date du jour. Re-dater
    /// au jour de l'appel déplaçait la dépense d'un mois à l'autre dans les rapports
    /// à chaque enregistrement de la phase (recette GPA, DEF-021).
    /// </param>
    protected async Task UpsertVehicleCostAsync(
        AccidentEvent ev,
        string type,
        decimal amount,
        DateTime? date,
        string description,
        CancellationToken ct)
    {
        // Véhicule renseigné ET existant : garanti par EnsureVehicleAsync, que la phase
        // appelle AVANT d'arriver ici. Aucun repli silencieux — tout le point du
        // correctif est que rien ne soit tu (recette Karim du 18/09/2026).
        var vehicleId = ev.VehicleId!.Value;

        var existing = await Context.VehicleCosts
            .FirstOrDefaultAsync(c => c.AccidentEventId == ev.Id
                                   && c.Type == type
                                   && c.VehicleId == vehicleId, ct);

        if (existing != null)
        {
            existing.Amount = amount;
            if (date.HasValue) existing.Date = date.Value;
            existing.Description = description;
            Logger.LogDebug(
                "Phase auto-sync: updated VehicleCost {Cost} for accident {Accident} (type={Type}, amount={Amount})",
                existing.Id, ev.Id, type, amount);
            return;
        }

        // accident_events.vehicle_id n'a pas de clé étrangère : insérer une dépense sur
        // un véhicule supprimé violait la contrainte au SaveChanges — 500, rien
        // d'enregistré (recette GPA, DEF-021). D'où le contrôle préalable ci-dessus.
        var newCost = new VehicleCost
        {
            VehicleId = vehicleId,
            CompanyId = ev.CompanyId,
            AccidentEventId = ev.Id,
            Type = type,
            Description = description,
            Amount = amount,
            Date = date ?? DateTime.UtcNow,
            CreatedByUserId = TenantService.UserId,
            CreatedAt = DateTime.UtcNow,
        };
        Context.VehicleCosts.Add(newCost);
        Logger.LogInformation(
            "Phase auto-sync: inserted VehicleCost for accident {Accident} (type={Type}, amount={Amount})",
            ev.Id, type, amount);
    }

    protected static string? NormaliseSeverity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lc = raw.Trim().ToLowerInvariant();
        return lc switch
        {
            "minor" or "moderate" or "severe" or "total" => lc,
            _ => null,
        };
    }
}
