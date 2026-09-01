using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly GisDbContext _context;

    public SubscriptionsController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<SubscriptionType>>> GetSubscriptions()
    {
        var subscriptions = await _context.SubscriptionTypes
            .Where(s => s.IsActive)
            .OrderBy(s => s.YearlyPrice)
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SubscriptionType>> GetSubscription(int id)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(id);

        if (subscription == null)
            return NotFound();

        return Ok(subscription);
    }

    /// <summary>
    /// Statut LÉGER pour la bannière d'abonnement (poll 10 min côté client).
    /// Whitelisté par le SubscriptionExpirationMiddleware : reste joignable même
    /// bloqué, c'est lui qui alimente l'écran « abonnement suspendu/expiré ».
    /// level: none | warning (J-30, admins société) | danger (J-7 ou grâce, tous) | blocked.
    /// </summary>
    [HttpGet("banner")]
    public async Task<ActionResult> GetBanner()
    {
        var companyId = GetCompanyId();
        var company = await _context.Societes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company == null) return Ok(new { level = "none", reason = "active" });

        var s = GisAPI.Application.Common.SubscriptionPolicy.Evaluate(company, DateTime.UtcNow);
        return Ok(new
        {
            level = s.Level,
            reason = s.Reason,
            expiresAt = s.ExpiresAt,
            daysRemaining = s.DaysRemaining,
            graceDaysLeft = s.GraceDaysLeft
        });
    }

    [HttpGet("current")]
    public async Task<ActionResult> GetCurrentSubscription()
    {
        var companyId = GetCompanyId();

        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
            return NotFound();

        var vehicleCount = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .CountAsync();

        var userCount = await _context.Users
            .Where(u => u.CompanyId == companyId)
            .CountAsync();

        var deviceCount = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId)
            .CountAsync();

        var geofenceCount = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .CountAsync();

        // Montant dû. Recette 01/09/2026 : l'écran affichait 299 € (forfait
        // d'origine figé sur la société) au lieu de 3 € × 2 véhicules × 12 mois.
        // SubscriptionPricing recalcule à chaque lecture pour les plans tarifés
        // par véhicule — même formule que le bandeau facturation sys_admin.
        var plan = company.SubscriptionType;
        var nextPaymentAmount = GisAPI.Application.Common
            .SubscriptionPricing.AmountDue(company, vehicleCount);

        // En période d'essai gratuit : offre en libre-service, rien n'a jamais
        // été réglé, ET l'échéance posée a la taille d'un essai (≤ 31 jours
        // depuis le départ). Cette dernière borne évite d'étiqueter « essai »
        // un client GPA installé à la main avec un an d'échéance : ses
        // règlements se font hors application, LastPaymentAt peut rester vide
        // à vie (cf. commentaire de IsSelfServiceSubscription).
        var isTrial = GisAPI.Application.Features.Auth.Commands.Login
            .LoginCommandHandler.IsSelfServiceSubscription(company)
            && company.LastPaymentAt == null
            && company.SubscriptionExpiresAt.HasValue
            && (company.SubscriptionExpiresAt.Value - company.SubscriptionStartedAt).TotalDays <= 31;

        return Ok(new
        {
            SubscriptionType = company.SubscriptionType,
            NextPaymentAmount = nextPaymentAmount,
            PricePerVehicle = plan?.PricePerVehicle ?? false,
            IsTrial = isTrial,
            company.BillingCycle,
            company.SubscriptionStatus,
            company.LastPaymentAt,
            company.SubscriptionStartedAt,
            Usage = new
            {
                Vehicles = new { Current = vehicleCount, Max = company.SubscriptionType?.MaxVehicles ?? 0 },
                Users = new { Current = userCount, Max = company.SubscriptionType?.MaxUsers ?? 0 },
                Devices = new { Current = deviceCount, Max = company.SubscriptionType?.MaxGpsDevices ?? 0 },
                Geofences = new { Current = geofenceCount, Max = company.SubscriptionType?.MaxGeofences ?? 0 }
            },
            ExpiresAt = company.SubscriptionExpiresAt
        });
    }

    // ── Commandes d'abonnement (libre-service, offre GPA) ──
    //
    // Le client COMMANDE ; la plateforme confirme depuis /api/admin une fois le
    // règlement reçu hors application. Le montant est calculé côté serveur.

    /// <summary>Passe une commande pour l'offre en libre-service.</summary>
    [HttpPost("orders")]
    public async Task<ActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromServices] MediatR.IMediator mediator)
    {
        var result = await mediator.Send(
            new GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders.CreateSubscriptionOrderCommand(
                request.SubscriptionTypeId, request.BillingCycle));
        return Ok(result);
    }

    /// <summary>Les commandes de ma société, plus récentes en tête.</summary>
    [HttpGet("orders/mine")]
    public async Task<ActionResult> GetMyOrders([FromServices] MediatR.IMediator mediator)
    {
        var result = await mediator.Send(
            new GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders.GetMySubscriptionOrdersQuery());
        return Ok(result);
    }

    /// <summary>Annule ma commande, tant qu'elle est en attente.</summary>
    [HttpDelete("orders/{id}")]
    public async Task<ActionResult> CancelOrder(int id, [FromServices] MediatR.IMediator mediator)
    {
        await mediator.Send(
            new GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders.CancelMySubscriptionOrderCommand(id));
        return Ok(new { message = "Commande annulée." });
    }

    // SUPPRIMÉ — POST /api/subscriptions/upgrade
    //
    // Cette action changeait le plan de la société et repoussait la date
    // d'expiration de request.Months, sans AUCUN contrôle : la classe ne porte
    // que [Authorize], donc n'importe quel utilisateur connecté — un chauffeur,
    // un opérateur — pouvait s'attribuer le plan le plus complet pour la durée
    // de son choix. Aucune borne sur Months, aucune trace de paiement.
    //
    // Tant que les comptes n'étaient créés qu'à la main par un administrateur,
    // la porte restait dans le cercle des clients installés. Avec l'inscription
    // libre, elle devient publique : on crée un compte, on appelle l'endpoint,
    // on obtient tout gratuitement — et l'écran de paiement ne sert plus à rien.
    //
    // Le seul chemin légitime pour prolonger un abonnement reste
    // RenewSubscriptionCommand (POST /api/admin/company/{id}/mark-paid), côté
    // sys_admin, qui applique les durées et les prix du plan. C'est vers lui que
    // devra converger un futur encaissement en ligne.
}

/// <summary>Corps de commande : AUCUN montant — il est calculé côté serveur.</summary>
public record CreateOrderRequest(int SubscriptionTypeId, string BillingCycle);
