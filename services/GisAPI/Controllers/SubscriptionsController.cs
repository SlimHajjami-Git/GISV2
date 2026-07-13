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

        return Ok(new
        {
            SubscriptionType = company.SubscriptionType,
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

    [HttpPost("upgrade")]
    public async Task<ActionResult> UpgradeSubscription([FromBody] UpgradeRequest request)
    {
        var companyId = GetCompanyId();

        var company = await _context.Societes.FindAsync(companyId);
        if (company == null)
            return NotFound();

        var subscriptionType = await _context.SubscriptionTypes.FindAsync(request.SubscriptionTypeId);
        if (subscriptionType == null)
            return NotFound(new { message = "Subscription type not found" });

        company.SubscriptionTypeId = request.SubscriptionTypeId;
        company.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(request.Months);
        company.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Subscription upgraded successfully" });
    }
}

public record UpgradeRequest(int SubscriptionTypeId, int Months = 1);
