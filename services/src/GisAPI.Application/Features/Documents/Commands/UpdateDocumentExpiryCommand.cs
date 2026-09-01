using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Commands;

/// <summary>
/// Corrige l'échéance d'un document véhicule (date d'expiration) SANS créer de
/// renouvellement ni de dépense — contrairement à <c>RenewDocumentCommand</c>.
///
/// <para>Répond au besoin « bouton Modifier » de l'écran Échéances (recette
/// client du 01/09/2026) : rectifier une date saisie par erreur, ou renseigner
/// une échéance connue, sans passer par le flux payant de renouvellement.</para>
/// </summary>
public record UpdateDocumentExpiryCommand(
    int VehicleId,
    string DocumentType,
    DateTime ExpiryDate) : IRequest<Unit>;

public class UpdateDocumentExpiryCommandHandler
    : IRequestHandler<UpdateDocumentExpiryCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateDocumentExpiryCommandHandler(
        IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Unit> Handle(UpdateDocumentExpiryCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new InvalidOperationException("Company ID not set");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        // Fin de journée UTC : une échéance « au 15/08 » reste valable le 15/08.
        var expiry = DateTime.SpecifyKind(
            request.ExpiryDate.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);

        switch (request.DocumentType)
        {
            case "insurance": vehicle.InsuranceExpiry = expiry; break;
            case "technical_inspection": vehicle.TechnicalInspectionExpiry = expiry; break;
            case "tax": vehicle.TaxExpiry = expiry; break;
            case "registration": vehicle.RegistrationExpiry = expiry; break;
            case "transport_permit": vehicle.TransportPermitExpiry = expiry; break;
            default:
                throw new DomainException(
                    $"Type de document « {request.DocumentType} » non modifiable ici.");
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
