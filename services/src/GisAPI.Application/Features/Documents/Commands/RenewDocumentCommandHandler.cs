using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Commands;

public class RenewDocumentCommandHandler : IRequestHandler<RenewDocumentCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly string[] ValidDocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit" };

    public RenewDocumentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
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

        // Create VehicleCost record for the renewal
        var cost = new VehicleCost
        {
            VehicleId = request.VehicleId,
            Type = request.DocumentType,
            Description = $"Renouvellement {GetDocumentTypeLabel(request.DocumentType)}" + 
                         (string.IsNullOrEmpty(request.Provider) ? "" : $" - {request.Provider}"),
            Amount = request.Amount,
            Date = request.PaymentDate,
            ExpiryDate = request.NewExpiryDate,
            DocumentNumber = request.DocumentNumber,
            DocumentUrl = request.DocumentUrl,
            CompanyId = companyId
        };

        _context.VehicleCosts.Add(cost);

        // Update vehicle expiry date based on document type
        switch (request.DocumentType)
        {
            case "insurance":
                vehicle.InsuranceExpiry = request.NewExpiryDate;
                break;
            case "technical_inspection":
                vehicle.TechnicalInspectionExpiry = request.NewExpiryDate;
                break;
            case "tax":
                vehicle.TaxExpiry = request.NewExpiryDate;
                break;
            case "registration":
                vehicle.RegistrationExpiry = request.NewExpiryDate;
                break;
            case "transport_permit":
                vehicle.TransportPermitExpiry = request.NewExpiryDate;
                break;
        }

        vehicle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

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



