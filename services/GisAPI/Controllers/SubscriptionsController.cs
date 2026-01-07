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
    public async Task<ActionResult<List<Subscription>>> GetSubscriptions()
    {
        var subscriptions = await _context.Subscriptions
            .Where(s => s.IsActive)
            .OrderBy(s => s.Price)
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Subscription>> GetSubscription(int id)
    {
        var subscription = await _context.Subscriptions.FindAsync(id);

        if (subscription == null)
            return NotFound();

        return Ok(subscription);
    }

    [HttpGet("current")]
    public async Task<ActionResult> GetCurrentSubscription()
    {
        var companyId = GetCompanyId();

        var company = await _context.Companies
            .Include(c => c.Subscription)
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
            Subscription = company.Subscription,
            Usage = new
            {
                Vehicles = new { Current = vehicleCount, Max = company.Subscription?.MaxVehicles ?? 0 },
                Users = new { Current = userCount, Max = company.Subscription?.MaxUsers ?? 0 },
                Devices = new { Current = deviceCount, Max = company.Subscription?.MaxGpsDevices ?? 0 },
                Geofences = new { Current = geofenceCount, Max = company.Subscription?.MaxGeofences ?? 0 }
            },
            ExpiresAt = company.SubscriptionExpiresAt
        });
    }

    [HttpPost("upgrade")]
    public async Task<ActionResult> UpgradeSubscription([FromBody] UpgradeRequest request)
    {
        var companyId = GetCompanyId();

        var company = await _context.Companies.FindAsync(companyId);
        if (company == null)
            return NotFound();

        var subscription = await _context.Subscriptions.FindAsync(request.SubscriptionId);
        if (subscription == null)
            return NotFound(new { message = "Subscription not found" });

        company.SubscriptionId = request.SubscriptionId;
        company.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(request.Months);
        company.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Subscription upgraded successfully" });
    }
}

public record UpgradeRequest(int SubscriptionId, int Months = 1);
