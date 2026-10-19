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

/// <summary>
/// Sortie de l'état « pending » (validation, rejet, annulation, remplacement) en UNE
/// écriture conditionnelle : UPDATE … WHERE status = 'pending'. De deux traitements
/// simultanés — deux opérateurs qui valident, le client qui annule pendant la
/// validation, une confirmation répétée du futur prestataire de paiement — un seul
/// trouve encore la commande en attente ; l'autre attend le verrou de la ligne puis
/// ne trouve plus rien. Avant (lecture, contrôle, puis écriture), les deux passaient
/// et l'abonnement était prolongé deux fois pour un seul règlement (préparation du
/// paiement en ligne, 19/10/2026).
/// </summary>
internal static class SubscriptionOrderTransitions
{
    /// <summary>
    /// Fait passer la commande de « pending » à <paramref name="status"/>. false si elle
    /// n'était plus en attente (ou pas à cette société). Note et auteur inchangés s'ils
    /// sont null. La copie suivie par le contexte n'est PAS rafraîchie ici : dans une
    /// transaction, l'appelant le fait après le COMMIT (voir <see cref="RefreshTrackedAsync"/>).
    /// </summary>
    internal static async Task<bool> TryLeavePendingAsync(
        IGisDbContext db, int orderId, int? companyId, string status,
        string? note, int? processedByUserId, DateTime now, CancellationToken ct)
    {
        var query = db.SubscriptionOrders.IgnoreQueryFilters()
            .Where(o => o.Id == orderId && o.Status == "pending");
        if (companyId is int company)
            query = query.Where(o => o.CompanyId == company);

        var updated = await query.ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, status)
            .SetProperty(o => o.Note, o => note ?? o.Note)
            .SetProperty(o => o.ProcessedAt, now)
            .SetProperty(o => o.ProcessedByUserId, o => processedByUserId ?? o.ProcessedByUserId)
            .SetProperty(o => o.UpdatedAt, now), ct);
        return updated == 1;
    }

    /// <summary>
    /// L'écriture conditionnelle passe à côté du suivi des entités : une copie déjà
    /// chargée dans le contexte garderait l'ancien statut. On la réaligne sur la base.
    /// </summary>
    internal static async Task RefreshTrackedAsync(IGisDbContext db, int orderId, CancellationToken ct)
    {
        var entry = db.ChangeTracker.Entries<SubscriptionOrder>().FirstOrDefault(e => e.Entity.Id == orderId);
        if (entry == null) return;
        var fresh = await db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (fresh == null) return;
        entry.CurrentValues.SetValues(fresh);
        entry.OriginalValues.SetValues(fresh);
        entry.State = EntityState.Unchanged;
    }

    internal static Task<string> StatusAsync(IGisDbContext db, int orderId, CancellationToken ct) =>
        db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync(ct);

    /// <summary>Refus d'un traitement sur une commande qui n'est plus en attente, en français.</summary>
    internal static string AlreadyProcessed(string status) => status switch
    {
        "confirmed" => "Cette commande a déjà été validée.",
        "rejected" => "Cette commande a déjà été rejetée.",
        "cancelled" => "Cette commande a déjà été annulée par le client.",
        _ => $"Cette commande a déjà été traitée (« {status} »)."
    };
}

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
///
/// ReplaceOrderId : la commande en attente que cette commande REMPLACE (le client
/// change de formule sans annuler d'abord). C'est celle qu'il voit à l'écran ; si
/// elle a été traitée entre-temps, la commande est refusée.
/// </summary>
public record CreateSubscriptionOrderCommand(
    int SubscriptionTypeId,
    string BillingCycle,
    int? ReplaceOrderId = null
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
            "semiannual" => plan.SemiannualPrice,
            "yearly" => plan.YearlyPrice,
            _ => throw new DomainException("Cycle de facturation invalide.")
        };

        // Un cycle à 0 n'est pas « gratuit », c'est un tarif non renseigné sur le
        // plan : commander gratuitement un abonnement serait un cadeau involontaire.
        if (amount <= 0m)
            throw new DomainException("Ce cycle de facturation n'est pas disponible pour cette offre.");

        // Plan tarifé PAR VÉHICULE : le prix du cycle s'entend par véhicule,
        // la commande porte donc prix × parc réel (au moins 1). Le filtre de
        // tenant scope déjà Vehicles sur la société du client connecté.
        if (plan.PricePerVehicle)
        {
            var vehicleCount = await _context.Vehicles
                .CountAsync(v => v.CompanyId == companyId, ct);
            amount *= Math.Max(1, vehicleCount);
        }

        // Une seule commande en attente à la fois : deux commandes validées coup
        // sur coup factureraient deux fois. Changer de formule REMPLACE la commande
        // en attente (préparation du paiement en ligne, 19/10/2026) : le client
        // n'annule plus d'abord, mais il désigne la commande qu'il remplace. Une
        // commande traitée entre-temps (validée par la plateforme) n'est jamais
        // remplacée en silence : il paierait deux fois.
        var pending = await _context.SubscriptionOrders.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.Status == "pending")
            .ToListAsync(ct);
        SubscriptionOrder? replaced = null;
        if (request.ReplaceOrderId is int replaceId)
        {
            replaced = pending.FirstOrDefault(o => o.Id == replaceId)
                ?? throw new DomainException(
                    "Votre commande en attente vient d'être traitée ou annulée. "
                    + "Rechargez la page avant de commander.");
            if (replaced.SubscriptionTypeId == plan.Id && replaced.BillingCycle == cycle && replaced.Amount == amount)
                throw new DomainException("Cette formule est déjà celle de votre commande en attente.");
            pending.Remove(replaced);
        }
        if (pending.Count > 0)
            throw new DomainException(
                "Vous avez déjà une commande en attente de validation. "
                + "Rechargez la page : vous pourrez l'annuler ou choisir une autre formule pour la remplacer.");

        SubscriptionOrder NewOrder() => new()
        {
            CompanyId = companyId,
            SubscriptionTypeId = plan.Id,
            BillingCycle = cycle,
            Amount = amount,
            Status = "pending",
            CreatedByUserId = userId
        };

        SubscriptionOrder order;
        if (replaced == null)
        {
            order = NewOrder();
            _context.SubscriptionOrders.Add(order);
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            // L'ancienne commande annulée et la nouvelle créée ensemble, ou rien. Si la
            // plateforme l'a validée entre-temps, l'annulation conditionnelle échoue et
            // rien n'est créé : le client ne paiera pas deux fois.
            var strategy = _context.Database.CreateExecutionStrategy();
            order = await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                // « cancelled » (c'est le client qui renonce), motif lisible côté plateforme.
                if (!await SubscriptionOrderTransitions.TryLeavePendingAsync(_context, replaced.Id, companyId,
                        "cancelled", "Remplacée par le client par une autre formule.", null, DateTime.UtcNow, ct))
                    throw new DomainException(
                        "Votre commande en attente vient d'être traitée ou annulée. "
                        + "Rechargez la page avant de commander.");
                var created = NewOrder();
                _context.SubscriptionOrders.Add(created);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return created;
            });
            await SubscriptionOrderTransitions.RefreshTrackedAsync(_context, replaced.Id, ct);
        }

        _logger.LogInformation(
            "Commande d'abonnement #{OrderId} : société {CompanyId}, plan {Plan}, cycle {Cycle}, {Amount}{Remplace}",
            order.Id, companyId, plan.Code, cycle, amount,
            replaced != null ? $" (remplace #{replaced.Id})" : "");

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
        var order = await _context.SubscriptionOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.CompanyId == companyId, ct)
            ?? throw new DomainException("Commande introuvable.");

        // Conditionnelle : une validation en cours par la plateforme l'emporte, ou
        // l'inverse — jamais une commande annulée ET un abonnement prolongé.
        if (order.Status != "pending"
            || !await SubscriptionOrderTransitions.TryLeavePendingAsync(_context, order.Id, companyId,
                    "cancelled", null, null, DateTime.UtcNow, ct))
            throw new DomainException("Cette commande a déjà été traitée, elle ne peut plus être annulée.");

        await SubscriptionOrderTransitions.RefreshTrackedAsync(_context, order.Id, ct);
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
        var strategy = _context.Database.CreateExecutionStrategy();
        var attempt = 0;
        var order = await strategy.ExecuteAsync(async () =>
        {
            // Nouvelle tentative après une erreur passagère : repartir de la base.
            if (attempt++ > 0) _context.ChangeTracker.Clear();

            var current = await _context.SubscriptionOrders
                .IgnoreQueryFilters().AsNoTracking()
                .Include(o => o.SubscriptionType)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
                ?? throw new DomainException("Commande introuvable.");

            // Déjà validée : rien à refaire, l'abonnement n'est prolongé qu'une fois
            // (deuxième opérateur, confirmation répétée du futur prestataire, nouvelle
            // tentative après une coupure au moment du COMMIT).
            if (current.Status == "confirmed") return current;
            if (current.Status != "pending")
                throw new DomainException(SubscriptionOrderTransitions.AlreadyProcessed(current.Status));

            // Commande prise ET abonnement activé dans la MÊME transaction : si
            // l'activation échoue, tout est annulé et la commande redevient « en
            // attente », reconfirmable — jamais une commande soldée sans abonnement
            // livré, ni un abonnement prolongé deux fois.
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            if (!await SubscriptionOrderTransitions.TryLeavePendingAsync(_context, current.Id, null,
                    "confirmed", null, _tenant.UserId, now, ct))
            {
                await tx.RollbackAsync(ct);
                var status = await SubscriptionOrderTransitions.StatusAsync(_context, current.Id, ct);
                if (status == "confirmed") { current.Status = status; return current; }
                throw new DomainException(SubscriptionOrderTransitions.AlreadyProcessed(status));
            }

            await _mediator.Send(new RenewSubscriptionCommand(
                current.CompanyId, current.BillingCycle, current.SubscriptionTypeId), ct);
            await tx.CommitAsync(ct);

            current.Status = "confirmed";
            current.ProcessedAt = now;
            current.ProcessedByUserId = _tenant.UserId;
            current.UpdatedAt = now;
            _logger.LogInformation(
                "Commande #{OrderId} confirmée : société {CompanyId}, plan {Plan}, {Amount}",
                current.Id, current.CompanyId, current.SubscriptionType?.Code, current.Amount);
            return current;
        });
        await SubscriptionOrderTransitions.RefreshTrackedAsync(_context, order.Id, ct);

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
            .IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
            ?? throw new DomainException("Commande introuvable.");

        if (order.Status != "pending")
            throw new DomainException(SubscriptionOrderTransitions.AlreadyProcessed(order.Status));

        // « rejected » et non « cancelled » : l'annulation vient du client, le
        // rejet vient de la plateforme — l'écran client n'affiche le motif que
        // pour les rejets, il ne doit pas confondre les deux.
        var note = string.IsNullOrWhiteSpace(request.Reason) ? "Rejetée par la plateforme." : request.Reason.Trim();
        if (!await SubscriptionOrderTransitions.TryLeavePendingAsync(_context, order.Id, null,
                "rejected", note, _tenant.UserId, DateTime.UtcNow, ct))
            throw new DomainException(SubscriptionOrderTransitions.AlreadyProcessed(
                await SubscriptionOrderTransitions.StatusAsync(_context, order.Id, ct)));

        await SubscriptionOrderTransitions.RefreshTrackedAsync(_context, order.Id, ct);
        return true;
    }
}
