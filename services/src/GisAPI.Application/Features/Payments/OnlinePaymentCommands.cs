using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Payments;

/// <summary>
/// Le client règle SA commande en attente par carte : la réponse porte la page ClicToPay où
/// l'envoyer. Aucun montant dans la requête : il vient de la commande, figé côté serveur.
/// </summary>
/// <param name="PageView">DESKTOP ou MOBILE (gabarit de la page de paiement).</param>
public record StartSubscriptionOrderPaymentCommand(int OrderId, string? PageView) : ICommand<StartOnlinePaymentDto>;

public class StartSubscriptionOrderPaymentCommandHandler
    : IRequestHandler<StartSubscriptionOrderPaymentCommand, StartOnlinePaymentDto>
{
    private readonly OnlinePaymentProcessor _processor;
    private readonly ICurrentTenantService _tenant;

    public StartSubscriptionOrderPaymentCommandHandler(OnlinePaymentProcessor processor, ICurrentTenantService tenant)
    {
        _processor = processor;
        _tenant = tenant;
    }

    public Task<StartOnlinePaymentDto> Handle(StartSubscriptionOrderPaymentCommand request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société introuvable pour l'utilisateur courant.");
        return _processor.StartAsync(companyId, _tenant.UserId, request.OrderId, request.PageView, ct);
    }
}

/// <summary>
/// Écran de retour : vérifie la tentative <paramref name="Reference"/> auprès de ClicToPay. Le
/// retour du navigateur ne prouve rien ; c'est cette vérification qui valide la commande.
/// </summary>
public record VerifyOnlinePaymentCommand(string Reference) : ICommand<OnlinePaymentResultDto>;

public class VerifyOnlinePaymentCommandHandler : IRequestHandler<VerifyOnlinePaymentCommand, OnlinePaymentResultDto>
{
    private readonly IGisDbContext _db;
    private readonly OnlinePaymentProcessor _processor;
    private readonly ICurrentTenantService _tenant;

    public VerifyOnlinePaymentCommandHandler(IGisDbContext db, OnlinePaymentProcessor processor, ICurrentTenantService tenant)
    {
        _db = db;
        _processor = processor;
        _tenant = tenant;
    }

    public async Task<OnlinePaymentResultDto> Handle(VerifyOnlinePaymentCommand request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société introuvable pour l'utilisateur courant.");
        var reference = (request.Reference ?? "").Trim();
        // CompanyId explicite : la référence d'une autre société répond « introuvable ».
        var paymentId = await _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.OrderNumber == reference && p.CompanyId == companyId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("Paiement introuvable.");
        return await _processor.VerifyAsync(paymentId, ct);
    }
}

/// <summary>Une tentative de paiement de MA société, sans rien de la carte hors le numéro masqué.</summary>
public record OnlinePaymentListItemDto(
    int Id,
    string Reference,
    int? OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? CardPanMasked,
    DateTime CreatedAt,
    DateTime? PaidAt);

/// <summary>Les 20 dernières tentatives de paiement de MA société.</summary>
public record GetMyOnlinePaymentsQuery : IQuery<List<OnlinePaymentListItemDto>>;

public class GetMyOnlinePaymentsQueryHandler : IRequestHandler<GetMyOnlinePaymentsQuery, List<OnlinePaymentListItemDto>>
{
    private readonly IGisDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public GetMyOnlinePaymentsQueryHandler(IGisDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<List<OnlinePaymentListItemDto>> Handle(GetMyOnlinePaymentsQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        var rows = await _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Kind == OnlinePayment.KindSubscription)
            .OrderByDescending(p => p.Id)
            .Take(20)
            .ToListAsync(ct);
        return rows.Select(p => new OnlinePaymentListItemDto(
            p.Id, p.OrderNumber, p.SubscriptionOrderId,
            ClicToPayAmounts.FromMinorUnits(p.AmountMinor, p.Currency), p.Currency,
            p.Status, p.CardPanMasked, p.CreatedAt, p.PaidAt)).ToList();
    }
}
