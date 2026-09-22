using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Costs;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;

public class SetSocieteScanQuotaCommandHandler : IRequestHandler<SetSocieteScanQuotaCommand, InvoiceScanCreditStatus>
{
    private readonly IGisDbContext _context;

    /// <summary>Message du refus d'une valeur hors bornes (affiché tel quel par la fiche).</summary>
    public const string InvalidTokensMessage =
        "Le crédit IA doit être entre 0 et 10 000 000 jetons (ou vide pour le défaut).";

    public SetSocieteScanQuotaCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<InvoiceScanCreditStatus> Handle(SetSocieteScanQuotaCommand request, CancellationToken ct)
    {
        // DomainException (400 + message affiché par la fiche) et non ArgumentException :
        // ExceptionHandlingMiddleware ne connaît pas cette dernière et répondait 500
        // « An unexpected error occurred » à une simple valeur hors bornes.
        if (request.MonthlyTokens is < 0 or > InvoiceScanCredit.MaxMonthlyTokens)
            throw new DomainException(InvalidTokensMessage);

        var societe = await _context.Societes
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException("Societe", request.Id);

        societe.InvoiceScanMonthlyTokens = request.MonthlyTokens;
        // L'ancien quota en SCANS n'est lu qu'à défaut de crédit en jetons. Le laisser en
        // place ferait qu'un retour au défaut (null) réactiverait silencieusement l'ancien
        // réglage (ex. 50 scans → 150 000 jetons) au lieu des 60 000 annoncés à l'écran :
        // il passe donc à NULL dans ce cas. Pour un crédit renseigné, il reçoit son
        // équivalent en scans (0 reste 0) — jamais relu ici, mais c'est tout ce que lit
        // l'ancien pod après un retour arrière : l'effacer rouvrait le scan à une société
        // désactivée.
        societe.InvoiceScanMonthlyLimit = InvoiceScanCredit.LegacyScanLimitShadow(request.MonthlyTokens);
        societe.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return await InvoiceScanCredit.LoadAsync(_context, societe.Id, DateTime.UtcNow, ct);
    }
}
