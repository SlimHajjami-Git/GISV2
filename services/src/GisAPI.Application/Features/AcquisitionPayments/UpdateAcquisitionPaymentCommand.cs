using FluentValidation;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Changement de statut d'une échéance (PUT /api/acquisition-payments/{id}).
/// <list type="bullet">
/// <item>paid : PaidAt ??= maintenant, PaidAmount ??= montant prévu (les valeurs
/// fournies priment).</item>
/// <item>planned : PaidAt et PaidAmount effacés — la quittance RESTE.</item>
/// <item>skipped : PaidAt / PaidAmount inchangés.</item>
/// </list>
/// La note n'est modifiée que si elle est fournie (chaîne vide = effacée).
/// 404 si la ligne n'appartient pas à la société ou n'est pas visible par
/// l'appelant (VehicleScope).
/// </summary>
public record UpdateAcquisitionPaymentCommand(
    int Id,
    string Status,
    DateTime? PaidAt = null,
    decimal? PaidAmount = null,
    string? Note = null) : IRequest<AcquisitionPaymentDto>;

public class UpdateAcquisitionPaymentValidator : AbstractValidator<UpdateAcquisitionPaymentCommand>
{
    public UpdateAcquisitionPaymentValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => AcquisitionPayment.Statuses.All.Contains(s))
            .WithMessage("Statut invalide : planned, paid ou skipped attendu");
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0)
            .When(x => x.PaidAmount.HasValue)
            .WithMessage("Le montant réglé ne peut pas être négatif");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public class UpdateAcquisitionPaymentHandler : IRequestHandler<UpdateAcquisitionPaymentCommand, AcquisitionPaymentDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;

    public UpdateAcquisitionPaymentHandler(IGisDbContext context, ICurrentTenantService tenantService, IDateTimeProvider clock)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
    }

    public async Task<AcquisitionPaymentDto> Handle(UpdateAcquisitionPaymentCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");
        var now = _clock.UtcNow;

        var payment = await _context.AcquisitionPayments
            .Include(p => p.Vehicle)
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AcquisitionPayment", request.Id);

        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        if (scope != null && !scope.Contains(payment.VehicleId))
            throw new NotFoundException("AcquisitionPayment", request.Id);

        switch (request.Status)
        {
            case AcquisitionPayment.Statuses.Paid:
                if (request.PaidAt.HasValue) payment.PaidAt = AsUtc(request.PaidAt.Value);
                if (request.PaidAmount.HasValue) payment.PaidAmount = request.PaidAmount;
                payment.PaidAt ??= now;
                payment.PaidAmount ??= payment.Amount;
                break;
            case AcquisitionPayment.Statuses.Planned:
                payment.PaidAt = null;
                payment.PaidAmount = null;
                break;
            case AcquisitionPayment.Statuses.Skipped:
                break;
            default:
                throw new DomainException($"Statut invalide : {request.Status}");
        }

        payment.Status = request.Status;
        if (request.Note != null)
            payment.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        payment.UpdatedAt = now;

        await _context.SaveChangesAsync(ct);

        var vehicle = payment.Vehicle
            ?? await _context.Vehicles.AsNoTracking().FirstAsync(v => v.Id == payment.VehicleId, ct);
        return AcquisitionPaymentDto.From(payment, vehicle, DateOnly.FromDateTime(now.Date));
    }

    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
}
