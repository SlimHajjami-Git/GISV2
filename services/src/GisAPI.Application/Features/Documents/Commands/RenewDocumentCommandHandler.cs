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
        // Validate document type
        if (!ValidDocumentTypes.Contains(request.DocumentType))
            throw new ArgumentException($"Invalid document type: {request.DocumentType}");

        CheckLength(request.DocumentNumber, DocumentNumberMaxLength, "Numéro de document");
        CheckLength(request.Provider, ProviderMaxLength, "Fournisseur");
        CheckLength(request.Notes, NotesMaxLength, "Notes");
        CheckLength(request.DocumentUrl, DocumentUrlMaxLength, "Lien du justificatif");

        // Get vehicle
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException($"Vehicle not found: {request.VehicleId}");

        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Convert dates to UTC
        var paymentDateUtc = DateTime.SpecifyKind(request.PaymentDate, DateTimeKind.Utc);
        var expiryDateUtc = DateTime.SpecifyKind(request.NewExpiryDate, DateTimeKind.Utc);

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

        // Update vehicle expiry date based on document type
        switch (request.DocumentType)
        {
            case "insurance":
                vehicle.InsuranceExpiry = expiryDateUtc;
                break;
            case "technical_inspection":
                vehicle.TechnicalInspectionExpiry = expiryDateUtc;
                break;
            case "tax":
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



