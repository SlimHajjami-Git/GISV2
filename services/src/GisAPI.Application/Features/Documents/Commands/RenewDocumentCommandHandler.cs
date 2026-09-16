using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Documents.Commands;

public class RenewDocumentCommandHandler : IRequestHandler<RenewDocumentCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<RenewDocumentCommandHandler> _logger;
    private static readonly string[] ValidDocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit" };

    // Tailles des colonnes de vehicle_costs qui reçoivent la saisie (receipt_number,
    // provider, notes, receipt_url). Au-delà, PostgreSQL refusait l'INSERT et
    // l'utilisateur recevait une erreur 500 sans explication.
    public const int DocumentNumberMaxLength = 100;
    public const int ProviderMaxLength = 200;
    public const int NotesMaxLength = 1000;
    public const int DocumentUrlMaxLength = 500;

    public RenewDocumentCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        ILogger<RenewDocumentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<int> Handle(RenewDocumentCommand request, CancellationToken cancellationToken)
    {
        // Refus métier en DomainException / NotFoundException (400 / 404 avec le
        // message) : ArgumentException et InvalidOperationException tombaient
        // en 500 « An unexpected error occurred » (DEF-031).
        if (!ValidDocumentTypes.Contains(request.DocumentType))
            throw new DomainException(InvalidTypeMessage(request.DocumentType));

        CheckLength(request.DocumentNumber, DocumentNumberMaxLength, "Numéro de document");
        CheckLength(request.Provider, ProviderMaxLength, "Fournisseur");
        CheckLength(request.Notes, NotesMaxLength, "Notes");
        CheckLength(request.DocumentUrl, DocumentUrlMaxLength, "Lien du justificatif");

        var companyId = _tenantService.CompanyId ?? throw new DomainException("Société non identifiée");

        // Borne explicite à la société, comme la correction d'échéance : le
        // filtre global multi-tenance est contourné pour les administrateurs système.
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, cancellationToken)
            ?? throw new DocumentVehiculeIntrouvableException(request.VehicleId);

        // Convert dates to UTC. L'échéance est enregistrée à minuit UTC, comme
        // la fiche véhicule et la correction d'échéance (ExpiryCalendar).
        var paymentDateUtc = DateTime.SpecifyKind(request.PaymentDate, DateTimeKind.Utc);
        var expiryDateUtc = ExpiryCalendar.ToStored(request.NewExpiryDate);

        // Capture pre-update value for diagnostic
        var oldExpiry = request.DocumentType switch
        {
            "insurance" => vehicle.InsuranceExpiry,
            "technical_inspection" => vehicle.TechnicalInspectionExpiry,
            "tax" => vehicle.TaxExpiry,
            "registration" => vehicle.RegistrationExpiry,
            "transport_permit" => vehicle.TransportPermitExpiry,
            _ => null
        };

        _logger.LogInformation(
            "Renewing {Type} for vehicle {VehicleId} (company {CompanyId}): {OldExpiry} → {NewExpiry}",
            request.DocumentType, vehicle.Id, vehicle.CompanyId, oldExpiry, expiryDateUtc);

        // Create VehicleCost record for the renewal. Montant absent ou nul :
        // la date d'expiration est mise a jour SANS creer de depense — une
        // ligne a zero fausserait les totaux (recette client du 26/08/2026).
        VehicleCost? cost = null;
        if (request.Amount > 0)
        cost = new VehicleCost
        {
            VehicleId = request.VehicleId,
            Type = request.DocumentType,
            Description = BuildDescription(request.DocumentType, request.Provider),
            Amount = request.Amount,
            Date = paymentDateUtc,
            // Toute la saisie de la fenêtre de renouvellement est conservée :
            // échéance, fournisseur et notes ont leur colonne depuis la migration
            // 047 ; le numéro de police et le justificatif vont dans
            // receipt_number / receipt_url, les colonnes de cette nature. Avant
            // cela, seule la description restait — la saisie disparaissait sans
            // message et l'historique rendait la description comme fournisseur.
            ExpiryDate = expiryDateUtc,
            Provider = request.Provider,
            Notes = request.Notes,
            ReceiptNumber = request.DocumentNumber,
            ReceiptUrl = request.DocumentUrl,
            CompanyId = companyId
        };

        if (cost is not null) _context.VehicleCosts.Add(cost);

        // Update vehicle expiry date based on document type. Le début de période
        // est recalé avec l'échéance (DEF-032) : il restait celui de la période
        // précédente et l'écran affichait « 30/09/2025 → 13/09/2027 ». Carte
        // grise et autorisation de transport n'ont pas de colonne de début
        // (registration_date est la mise en circulation du véhicule).
        switch (request.DocumentType)
        {
            case "insurance":
                vehicle.InsuranceStartDate = RenewedPeriodStart(
                    vehicle.InsuranceStartDate, vehicle.InsuranceExpiry, paymentDateUtc, expiryDateUtc);
                vehicle.InsuranceExpiry = expiryDateUtc;
                break;
            case "technical_inspection":
                vehicle.TechnicalInspectionStartDate = RenewedPeriodStart(
                    vehicle.TechnicalInspectionStartDate, vehicle.TechnicalInspectionExpiry, paymentDateUtc, expiryDateUtc);
                vehicle.TechnicalInspectionExpiry = expiryDateUtc;
                break;
            case "tax":
                vehicle.TaxStartDate = RenewedPeriodStart(
                    vehicle.TaxStartDate, vehicle.TaxExpiry, paymentDateUtc, expiryDateUtc);
                vehicle.TaxExpiry = expiryDateUtc;
                break;
            case "registration":
                vehicle.RegistrationExpiry = expiryDateUtc;
                break;
            case "transport_permit":
                vehicle.TransportPermitExpiry = expiryDateUtc;
                break;
        }

        vehicle.UpdatedAt = DateTime.UtcNow;

        // Calypso 7 — defensive: verify EF actually picked up the modification
        // before saving. If the change tracker thinks the entity is Unchanged
        // (e.g. due to AsNoTracking or a stale instance), force it to Modified.
        // This guarantees the UPDATE statement is generated and prevents the
        // bug where the cost row was saved but the vehicle.expiry stayed stale.
        if (_context is DbContext dbCtx)
        {
            var entry = dbCtx.Entry(vehicle);
            if (entry.State == EntityState.Unchanged || entry.State == EntityState.Detached)
            {
                _logger.LogWarning(
                    "Vehicle {VehicleId} entry was {State} after property update — forcing Modified state.",
                    vehicle.Id, entry.State);
                entry.State = EntityState.Modified;
            }
        }

        var rowsAffected = await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Renewal saved: cost.Id={CostId}, rowsAffected={RowsAffected}, vehicle.{Type}Expiry now={NewExpiry}",
            cost?.Id ?? 0, rowsAffected, request.DocumentType, expiryDateUtc);

        // 0 = renouvellement sans depense (montant facultatif).
        return cost?.Id ?? 0;
    }

    /// <summary>
    /// Début de la période ouverte par un renouvellement, cohérent avec la
    /// nouvelle échéance.
    ///
    /// <para>La fenêtre de renouvellement n'envoie que la nouvelle échéance, pas
    /// la durée choisie : la durée renouvelée est celle de la période précédente
    /// (début → échéance, arrondie au mois), et le début vaut nouvelle échéance
    /// moins cette durée. Ce début n'est retenu que s'il tombe entre la date de
    /// paiement et l'ancienne échéance : renouvellement anticipé (la période
    /// part du paiement) ou dans la continuité (elle part de l'ancienne
    /// échéance). Hors de cet intervalle — durée différente, période précédente
    /// inconnue ou déjà incohérente — le début est le plus tardif de l'ancienne
    /// échéance et de la date de paiement : la nouvelle période ne recouvre pas
    /// l'ancienne et ne commence pas avant d'avoir été payée.</para>
    ///
    /// <para>Une nouvelle échéance antérieure à ce début (saisie rétroactive)
    /// garde une période de la durée précédente, ou l'ancien début si cette
    /// durée est inconnue : aucune date n'est inventée.</para>
    /// </summary>
    public static DateTime? RenewedPeriodStart(
        DateTime? oldStart, DateTime? oldExpiry, DateTime paymentDate, DateTime newExpiry)
    {
        var newEnd = ExpiryCalendar.Day(newExpiry);
        var paid = ExpiryCalendar.Day(paymentDate);
        DateTime? previousEnd = oldExpiry.HasValue ? ExpiryCalendar.Day(oldExpiry.Value) : null;
        var months = oldStart.HasValue && previousEnd.HasValue
            ? WholeMonths(ExpiryCalendar.Day(oldStart.Value), previousEnd.Value)
            : null;

        var latest = previousEnd.HasValue && previousEnd.Value > paid ? previousEnd.Value : paid;
        var earliest = previousEnd.HasValue && previousEnd.Value < paid ? previousEnd.Value : paid;

        var start = latest;
        if (months.HasValue)
        {
            var candidate = newEnd.AddMonths(-months.Value);
            if (candidate >= earliest && candidate <= latest)
                start = candidate;
        }

        if (start >= newEnd)
        {
            if (!months.HasValue) return oldStart;
            start = newEnd.AddMonths(-months.Value);
        }

        // Colonnes de début en timestamp sans fuseau, saisies au jour par la fiche véhicule.
        return DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
    }

    /// <summary>Durée en mois entiers (arrondie au plus proche) ; null sous un mois.</summary>
    private static int? WholeMonths(DateTime start, DateTime end)
    {
        if (end <= start) return null;

        var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
        var anchor = start.AddMonths(months);
        if (anchor > end && (end - start.AddMonths(months - 1)) < (anchor - end))
            months--;
        else if (anchor < end && (start.AddMonths(months + 1) - end) < (end - anchor))
            months++;

        return months >= 1 ? months : null;
    }

    /// <summary>
    /// Le renvoi vers la fiche du chauffeur n'a de sens que pour un permis : sur
    /// une faute de frappe, il égarait l'utilisateur.
    /// </summary>
    private static string InvalidTypeMessage(string? documentType)
    {
        var type = documentType ?? "";
        if (type == "driver_permit" || type.StartsWith("permis", StringComparison.OrdinalIgnoreCase))
            return $"Type de document « {type} » invalide. " +
                   "Un permis de conducteur se renouvelle depuis la fiche du chauffeur.";

        return $"Type de document « {type} » invalide (assurance, visite technique, vignette, " +
               "carte grise ou autorisation de transport).";
    }

    private static void CheckLength(string? value, int maxLength, string fieldLabel)
    {
        if (value != null && value.Length > maxLength)
            throw new DomainException(
                $"{fieldLabel} : {maxLength} caractères au maximum ({value.Length} saisis). Raccourcissez la saisie.");
    }

    private const string DescriptionPrefix = "Renouvellement ";
    private const string ProviderSeparator = " - ";

    private static string BuildDescription(string documentType, string? provider) =>
        DescriptionPrefix + GetDocumentTypeLabel(documentType) +
        (string.IsNullOrEmpty(provider) ? "" : ProviderSeparator + provider);

    /// <summary>
    /// Fournisseur d'une dépense de document dont la colonne provider est vide.
    /// Les renouvellements antérieurs à la migration 047 ne portent leur
    /// fournisseur que dans la description écrite par <see cref="BuildDescription"/> :
    /// on l'y relit. « Renouvellement Assurance » seul = aucun fournisseur saisi.
    /// Une description libre (dépense saisie ou scannée hors renouvellement) est
    /// rendue telle quelle, comme l'historique le faisait avant la migration.
    /// </summary>
    public static string? ProviderFromDescription(string documentType, string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var renewalLabel = DescriptionPrefix + GetDocumentTypeLabel(documentType);
        if (!description.StartsWith(renewalLabel, StringComparison.Ordinal))
            return description;

        var rest = description[renewalLabel.Length..];
        if (rest.Length == 0)
            return null;
        if (!rest.StartsWith(ProviderSeparator, StringComparison.Ordinal))
            return description;

        var provider = rest[ProviderSeparator.Length..].Trim();
        return provider.Length == 0 ? null : provider;
    }

    private static string GetDocumentTypeLabel(string type) => type switch
    {
        "insurance" => "Assurance",
        "technical_inspection" => "Visite technique",
        "tax" => "Vignette",
        "registration" => "Carte grise",
        "transport_permit" => "Autorisation transport",
        _ => type
    };
}



