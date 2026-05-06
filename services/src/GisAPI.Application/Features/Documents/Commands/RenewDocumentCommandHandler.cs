using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
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

        // Create VehicleCost record for the renewal
        var cost = new VehicleCost
        {
            VehicleId = request.VehicleId,
            Type = request.DocumentType,
            Description = $"Renouvellement {GetDocumentTypeLabel(request.DocumentType)}" +
                         (string.IsNullOrEmpty(request.Provider) ? "" : $" - {request.Provider}"),
            Amount = request.Amount,
            Date = paymentDateUtc,
            ExpiryDate = expiryDateUtc,
            DocumentNumber = request.DocumentNumber,
            DocumentUrl = request.DocumentUrl,
            CompanyId = companyId
        };

        _context.VehicleCosts.Add(cost);

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
            cost.Id, rowsAffected, request.DocumentType, expiryDateUtc);

        return cost.Id;
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



