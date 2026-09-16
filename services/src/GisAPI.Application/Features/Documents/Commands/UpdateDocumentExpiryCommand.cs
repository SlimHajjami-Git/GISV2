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
            ?? throw new DomainException("Société non identifiée");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new DocumentVehiculeIntrouvableException(request.VehicleId);

        // Minuit UTC, comme la fiche véhicule et le renouvellement : l'ancien
        // 23:59:59 décalait d'un jour les jours restants selon le chemin de
        // saisie. « Valable jusqu'au 15/08 inclus » est porté par le compte en
        // jours calendaires (ExpiryCalendar : 0 jour = bientôt, pas expiré).
        var expiry = ExpiryCalendar.ToStored(request.ExpiryDate);

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
