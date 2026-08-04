using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders;

/// <summary>Ligne de commande telle que les écrans (client et admin) la lisent.</summary>
public record SubscriptionOrderDto(
    int Id,
    int CompanyId,
    string CompanyName,
    int SubscriptionTypeId,
    string PlanName,
    string PlanCode,
    string BillingCycle,
    decimal Amount,
    string Status,
    string? Note,
    DateTime CreatedAt,
    DateTime? ProcessedAt);

// ─────────────────────────────────────────────────────────────────────────────
//  CÔTÉ CLIENT — passer, consulter, annuler sa commande
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Le client commande un abonnement depuis son écran.
///
/// AUCUN MONTANT dans la requête : il est calculé ici, depuis le plan et le
/// cycle. Un montant venu de l'écran serait un prix libre-service.
///
/// Seule l'offre GPA (plan-basique) est achetable en ligne pour l'instant — les
/// offres avec GPS impliquent du matériel, et pour l'installation une
/// intervention : elles se vendent avec un humain au bout du fil.
/// </summary>
public record CreateSubscriptionOrderCommand(
    int SubscriptionTypeId,
    string BillingCycle
) : ICommand<SubscriptionOrderDto>;

public class CreateSubscriptionOrderCommandHandler
    : IRequestHandler<CreateSubscriptionOrderCommand, SubscriptionOrderDto>
{
    /// <summary>Codes des plans achetables SANS intervention humaine.</summary>
    public static readonly string[] SelfPurchasablePlanCodes = { "plan-basique" };

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<CreateSubscriptionOrderCommandHandler> _logger;

    public CreateSubscriptionOrderCommandHandler(
        IGisDbContext context, ICurrentTenantService tenant,
        ILogger<CreateSubscriptionOrderCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<SubscriptionOrderDto> Handle(CreateSubscriptionOrderCommand request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId
            ?? throw new DomainException("Société introuvable pour l'utilisateur courant.");
        var userId = _tenant.UserId ?? 0;

        var plan = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(p => p.Id == request.SubscriptionTypeId && p.IsActive, ct)
            ?? throw new DomainException("Cette offre n'existe pas ou n'est plus proposée.");

        if (!SelfPurchasablePlanCodes.Contains(plan.Code))
            throw new DomainException(
                "Cette offre inclut du matériel GPS : contactez-nous pour la souscrire, "
                + "nous organisons la configuration avec vous.");

        var cycle = (request.BillingCycle ?? "").ToLowerInvariant();
        var amount = cycle switch
        {
            "monthly" => plan.MonthlyPrice,
            "quarterly" => plan.QuarterlyPrice,
            "yearly" => plan.YearlyPrice,
            _ => throw new DomainException("Cycle de facturation invalide.")
        };

        // Un cycle à 0 n'est pas « gratuit », c'est un tarif non renseigné sur le
        // plan : commander gratuitement un abonnement serait un cadeau involontaire.
        if (amount <= 0m)
            throw new DomainException("Ce cycle de facturation n'est pas disponible pour cette offre.");

        // Une seule commande en attente à la fois : deux commandes validées coup
        // sur coup factureraient deux fois. Le client modifie en annulant d'abord.
        var pending = await _context.SubscriptionOrders
            .AnyAsync(o => o.CompanyId == companyId && o.Status == "pending", ct);
        if (pending)
            throw new DomainException(
                "Vous avez déjà une commande en attente de validation. "
                + "Annulez-la d'abord si vous souhaitez la modifier.");

        var order = new SubscriptionOrder
        {
            CompanyId = companyId,
            SubscriptionTypeId = plan.Id,
            BillingCycle = cycle,
            Amount = amount,
            Status = "pending",
            CreatedByUserId = userId
        };
        _context.SubscriptionOrders.Add(order);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Commande d'abonnement #{OrderId} : société {CompanyId}, plan {Plan}, cycle {Cycle}, {Amount}",
            order.Id, companyId, plan.Code, cycle, amount);

        var companyName = await _context.Societes
            .Where(s => s.Id == companyId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";

        return ToDto(order, companyName, plan);
    }

    internal static SubscriptionOrderDto ToDto(SubscriptionOrder o, string companyName, SubscriptionType plan) =>
        new(o.Id, o.CompanyId, companyName, o.SubscriptionTypeId, plan.Name, plan.Code,
            o.BillingCycle, o.Amount, o.Status, o.Note, o.CreatedAt, o.ProcessedAt);
}

/// <summary>Les commandes de MA société, plus récentes en tête.</summary>
public record GetMySubscriptionOrdersQuery() : ICommand<List<SubscriptionOrderDto>>;

public class GetMySubscriptionOrdersQueryHandler
    : IRequestHandler<GetMySubscriptionOrdersQuery, List<SubscriptionOrderDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMySubscriptionOrdersQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<SubscriptionOrderDto>> Handle(GetMySubscriptionOrdersQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        return await _context.SubscriptionOrders
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new SubscriptionOrderDto(
                o.Id, o.CompanyId, "", o.SubscriptionTypeId,
                o.SubscriptionType!.Name, o.SubscriptionType.Code,
                o.BillingCycle, o.Amount, o.Status, o.Note, o.CreatedAt, o.ProcessedAt))
            .ToListAsync(ct);
    }
}

/// <summary>Le client annule SA commande, tant qu'elle est en attente.</summary>
public record CancelMySubscriptionOrderCommand(int OrderId) : ICommand<bool>;

public class CancelMySubscriptionOrderCommandHandler
    : IRequestHandler<CancelMySubscriptionOrderCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public CancelMySubscriptionOrderCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<bool> Handle(CancelMySubscriptionOrderCommand request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        // Le filtre CompanyId est EXPLICITE en plus du filtre global de tenant :
        // annuler la commande d'une autre société ne doit dépendre d'aucun
        // court-circuit sys_admin du filtre.
        var order = await _context.SubscriptionOrders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.CompanyId == companyId, ct)
            ?? throw new DomainException("Commande introuvable.");

        if (order.Status != "pending")
            throw new DomainException("Cette commande a déjà été traitée, elle ne peut plus être annulée.");

        order.Status = "cancelled";
        order.ProcessedAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  CÔTÉ PLATEFORME (sys_admin) — lister, confirmer, rejeter
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Toutes les commandes, en attente d'abord. Réservé au sys_admin (route /api/admin).</summary>
public record GetSubscriptionOrdersQuery(string? Status = null) : ICommand<List<SubscriptionOrderDto>>;

public class GetSubscriptionOrdersQueryHandler
    : IRequestHandler<GetSubscriptionOrdersQuery, List<SubscriptionOrderDto>>
{
    private readonly IGisDbContext _context;

    public GetSubscriptionOrdersQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<SubscriptionOrderDto>> Handle(GetSubscriptionOrdersQuery request, CancellationToken ct)
    {
        var query = _context.SubscriptionOrders.AsNoTracking().IgnoreQueryFilters();
        if (!string.IsNullOrEmpty(request.Status))
            query = query.Where(o => o.Status == request.Status);

        return await query
            .OrderBy(o => o.Status == "pending" ? 0 : 1)
            .ThenByDescending(o => o.CreatedAt)
            .Select(o => new SubscriptionOrderDto(
                o.Id, o.CompanyId,
                _context.Societes.Where(s => s.Id == o.CompanyId).Select(s => s.Name).FirstOrDefault() ?? "",
                o.SubscriptionTypeId, o.SubscriptionType!.Name, o.SubscriptionType.Code,
                o.BillingCycle, o.Amount, o.Status, o.Note, o.CreatedAt, o.ProcessedAt))
            .ToListAsync(ct);
    }
}

/// <summary>
/// La plateforme confirme la commande : LE RÈGLEMENT A ÉTÉ REÇU (hors application).
/// L'activation passe par RenewSubscriptionCommand — le même chemin que le
/// renouvellement manuel : plan appliqué, échéance prolongée depuis
/// max(maintenant, échéance courante), last_payment_at posé. Un futur prestataire
/// de paiement confirmera par webhook au lieu de l'opérateur ; ce chemin ne
/// bougera pas.
/// </summary>
public record ConfirmSubscriptionOrderCommand(int OrderId) : ICommand<SubscriptionOrderDto>;

public class ConfirmSubscriptionOrderCommandHandler
    : IRequestHandler<ConfirmSubscriptionOrderCommand, SubscriptionOrderDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IMediator _mediator;
    private readonly ILogger<ConfirmSubscriptionOrderCommandHandler> _logger;

    public ConfirmSubscriptionOrderCommandHandler(
        IGisDbContext context, ICurrentTenantService tenant, IMediator mediator,
        ILogger<ConfirmSubscriptionOrderCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<SubscriptionOrderDto> Handle(ConfirmSubscriptionOrderCommand request, CancellationToken ct)
    {
        var order = await _context.SubscriptionOrders
            .IgnoreQueryFilters()
            .Include(o => o.SubscriptionType)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
            ?? throw new DomainException("Commande introuvable.");

        if (order.Status != "pending")
            throw new DomainException($"Cette commande est déjà « {order.Status} ».");

        // L'ordre des écritures est délibéré : la commande passe à « confirmed »
        // APRÈS l'activation. Si l'activation échoue, la commande reste en attente
        // et peut être reconfirmée — l'inverse laisserait une commande soldée sans
        // abonnement livré.
        await _mediator.Send(new RenewSubscriptionCommand(
            order.CompanyId, order.BillingCycle, order.SubscriptionTypeId), ct);

        order.Status = "confirmed";
        order.ProcessedAt = DateTime.UtcNow;
        order.ProcessedByUserId = _tenant.UserId;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Commande #{OrderId} confirmée : société {CompanyId}, plan {Plan}, {Amount}",
            order.Id, order.CompanyId, order.SubscriptionType?.Code, order.Amount);

        var companyName = await _context.Societes.IgnoreQueryFilters()
            .Where(s => s.Id == order.CompanyId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "";
        return CreateSubscriptionOrderCommandHandler.ToDto(order, companyName, order.SubscriptionType!);
    }
}

/// <summary>La plateforme rejette la commande, avec un motif montré au client.</summary>
public record RejectSubscriptionOrderCommand(int OrderId, string? Reason) : ICommand<bool>;

public class RejectSubscriptionOrderCommandHandler
    : IRequestHandler<RejectSubscriptionOrderCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public RejectSubscriptionOrderCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<bool> Handle(RejectSubscriptionOrderCommand request, CancellationToken ct)
    {
        var order = await _context.SubscriptionOrders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
            ?? throw new DomainException("Commande introuvable.");

        if (order.Status != "pending")
            throw new DomainException($"Cette commande est déjà « {order.Status} ».");

        // « rejected » et non « cancelled » : l'annulation vient du client, le
        // rejet vient de la plateforme — l'écran client n'affiche le motif que
        // pour les rejets, il ne doit pas confondre les deux.
        order.Status = "rejected";
        order.Note = string.IsNullOrWhiteSpace(request.Reason) ? "Rejetée par la plateforme." : request.Reason.Trim();
        order.ProcessedAt = DateTime.UtcNow;
        order.ProcessedByUserId = _tenant.UserId;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
