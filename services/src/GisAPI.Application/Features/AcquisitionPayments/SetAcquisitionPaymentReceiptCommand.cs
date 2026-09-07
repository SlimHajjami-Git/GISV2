using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Rattache une quittance (fichier déjà écrit par le contrôleur sous
/// uploads/acquisition-receipts/{companyId}/) à une échéance. Renvoie
/// l'ancienne URL pour que le contrôleur supprime le fichier remplacé.
/// 404 si la ligne n'est pas à la société ou hors portée de l'appelant.
/// </summary>
public record SetAcquisitionPaymentReceiptCommand(int Id, string ReceiptUrl) : IRequest<string?>;

public class SetAcquisitionPaymentReceiptHandler : IRequestHandler<SetAcquisitionPaymentReceiptCommand, string?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SetAcquisitionPaymentReceiptHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<string?> Handle(SetAcquisitionPaymentReceiptCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var payment = await _context.AcquisitionPayments
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AcquisitionPayment", request.Id);

        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        if (scope != null && !scope.Contains(payment.VehicleId))
            throw new NotFoundException("AcquisitionPayment", request.Id);

        var previous = payment.ReceiptUrl;
        payment.ReceiptUrl = request.ReceiptUrl;
        payment.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return previous;
    }
}
