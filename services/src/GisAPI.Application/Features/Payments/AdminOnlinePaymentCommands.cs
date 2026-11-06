using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Payments;

// ─────────────────────────────────────────────────────────────────────────────
//  Console d'administration — éprouver ClicToPay et suivre les paiements (route /api/admin,
//  administrateur système). Rien ici ne touche une commande ni un abonnement.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Paiement de TEST d'un montant libre (borné), pour éprouver le compte marchand.</summary>
public record StartAdminTestPaymentCommand(decimal Amount, string? Currency) : ICommand<StartOnlinePaymentDto>;

public class StartAdminTestPaymentCommandHandler : IRequestHandler<StartAdminTestPaymentCommand, StartOnlinePaymentDto>
{
    private readonly OnlinePaymentProcessor _processor;
    private readonly ICurrentTenantService _tenant;

    public StartAdminTestPaymentCommandHandler(OnlinePaymentProcessor processor, ICurrentTenantService tenant)
    {
        _processor = processor;
        _tenant = tenant;
    }

    public Task<StartOnlinePaymentDto> Handle(StartAdminTestPaymentCommand request, CancellationToken ct)
    {
        // La tentative est rangée sous la société de l'administrateur (Belive) : la table exige une société.
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société de l'administrateur introuvable.");
        return _processor.StartAdminTestAsync(companyId, _tenant.UserId, request.Amount, request.Currency, ct);
    }
}

/// <summary>Vérifie n'importe quelle tentative (test ou client) auprès de ClicToPay.</summary>
public record VerifyAdminOnlinePaymentCommand(string Reference) : ICommand<OnlinePaymentResultDto>;

public class VerifyAdminOnlinePaymentCommandHandler : IRequestHandler<VerifyAdminOnlinePaymentCommand, OnlinePaymentResultDto>
{
    private readonly IGisDbContext _db;
    private readonly OnlinePaymentProcessor _processor;

    public VerifyAdminOnlinePaymentCommandHandler(IGisDbContext db, OnlinePaymentProcessor processor)
    {
        _db = db;
        _processor = processor;
    }

    public async Task<OnlinePaymentResultDto> Handle(VerifyAdminOnlinePaymentCommand request, CancellationToken ct)
    {
        var reference = (request.Reference ?? "").Trim();
        var paymentId = await _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrderNumber == reference)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Paiement introuvable.");
        return await _processor.VerifyAsync(paymentId, ct);
    }
}

/// <summary>Une tentative de paiement, toutes sociétés, telle que la console la liste.</summary>
public record AdminOnlinePaymentDto(
    int Id,
    string Reference,
    string Kind,
    int CompanyId,
    string CompanyName,
    int? OrderId,
    decimal Amount,
    string Currency,
    string Status,
    int? ProviderOrderStatus,
    string? ActionDescription,
    string? CardPanMasked,
    string? ApprovalCode,
    string? Note,
    DateTime CreatedAt,
    DateTime? PaidAt);

/// <summary>Les dernières tentatives, toutes sociétés, plus récentes en tête.</summary>
public record GetAdminOnlinePaymentsQuery(int Limit = 50, string? Kind = null) : IQuery<List<AdminOnlinePaymentDto>>;

public class GetAdminOnlinePaymentsQueryHandler : IRequestHandler<GetAdminOnlinePaymentsQuery, List<AdminOnlinePaymentDto>>
{
    private readonly IGisDbContext _db;

    public GetAdminOnlinePaymentsQueryHandler(IGisDbContext db) => _db = db;

    public async Task<List<AdminOnlinePaymentDto>> Handle(GetAdminOnlinePaymentsQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 200);
        var query = _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Kind))
            query = query.Where(p => p.Kind == request.Kind);

        var rows = await query.OrderByDescending(p => p.Id).Take(limit).ToListAsync(ct);
        var companyIds = rows.Select(r => r.CompanyId).Distinct().ToList();
        var names = await _db.Societes.IgnoreQueryFilters().AsNoTracking()
            .Where(s => companyIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rows.Select(p => new AdminOnlinePaymentDto(
            p.Id, p.OrderNumber, p.Kind, p.CompanyId, names.GetValueOrDefault(p.CompanyId, ""),
            p.SubscriptionOrderId, ClicToPayAmounts.FromMinorUnits(p.AmountMinor, p.Currency), p.Currency,
            p.Status, p.ProviderOrderStatus, p.ActionDescription, p.CardPanMasked, p.ApprovalCode,
            p.Note, p.CreatedAt, p.PaidAt)).ToList();
    }
}
