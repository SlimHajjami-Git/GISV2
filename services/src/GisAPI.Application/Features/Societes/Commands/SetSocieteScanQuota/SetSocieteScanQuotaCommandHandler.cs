using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;

public class SetSocieteScanQuotaCommandHandler : IRequestHandler<SetSocieteScanQuotaCommand, bool>
{
    private readonly IGisDbContext _context;

    public SetSocieteScanQuotaCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(SetSocieteScanQuotaCommand request, CancellationToken ct)
    {
        if (request.MonthlyLimit is < 0 or > 100_000)
            throw new ArgumentException("Le quota doit être entre 0 et 100000 (ou vide pour le défaut).");

        var societe = await _context.Societes
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException("Societe", request.Id);

        societe.InvoiceScanMonthlyLimit = request.MonthlyLimit;
        societe.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
