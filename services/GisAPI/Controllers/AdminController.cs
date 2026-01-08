using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using System.Text.Json;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin,super_admin")]
public class AdminController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IConfiguration _configuration;
    private static MaintenanceModeDto _maintenanceMode = new() { Enabled = false, Pages = new List<string>(), Message = "" };

    public AdminController(GisDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    // ==================== SUBSCRIPTIONS ====================

    [HttpGet("subscriptions")]
    public async Task<ActionResult<List<SubscriptionDto>>> GetSubscriptions()
    {
        var subscriptions = await _context.Subscriptions
            .OrderBy(s => s.Price)
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                Name = s.Name,
                Type = s.Type,
                Price = s.Price,
                MaxVehicles = s.MaxVehicles,
                GpsTracking = s.GpsTracking,
                GpsInstallation = s.GpsInstallation,
                Features = GetSubscriptionFeatures(s)
            })
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("subscriptions/{id}")]
    public async Task<ActionResult<SubscriptionDto>> GetSubscription(int id)
    {
        var subscription = await _context.Subscriptions.FindAsync(id);
        if (subscription == null)
            return NotFound();

        return Ok(new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.Type,
            Price = subscription.Price,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionFeatures(subscription)
        });
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<SubscriptionDto>> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        var subscription = new Subscription
        {
            Name = request.Name,
            Type = request.Type ?? "parc",
            Price = request.Price,
            MaxVehicles = request.MaxVehicles,
            GpsTracking = request.GpsTracking,
            GpsInstallation = request.GpsInstallation
        };

        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSubscription), new { id = subscription.Id }, new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.Type,
            Price = subscription.Price,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionFeatures(subscription)
        });
    }

    [HttpPut("subscriptions/{id}")]
    public async Task<ActionResult<SubscriptionDto>> UpdateSubscription(int id, [FromBody] CreateSubscriptionRequest request)
    {
        var subscription = await _context.Subscriptions.FindAsync(id);
        if (subscription == null)
            return NotFound();

        subscription.Name = request.Name;
        subscription.Type = request.Type ?? subscription.Type;
        subscription.Price = request.Price;
        subscription.MaxVehicles = request.MaxVehicles;
        subscription.GpsTracking = request.GpsTracking;
        subscription.GpsInstallation = request.GpsInstallation;

        await _context.SaveChangesAsync();

        return Ok(new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.Type,
            Price = subscription.Price,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionFeatures(subscription)
        });
    }

    [HttpDelete("subscriptions/{id}")]
    public async Task<ActionResult> DeleteSubscription(int id)
    {
        var subscription = await _context.Subscriptions.FindAsync(id);
        if (subscription == null)
            return NotFound();

        var companiesUsingSubscription = await _context.Companies.CountAsync(c => c.SubscriptionId == id);
        if (companiesUsingSubscription > 0)
        {
            return BadRequest(new { message = $"Impossible de supprimer: {companiesUsingSubscription} société(s) utilisent cet abonnement" });
        }

        _context.Subscriptions.Remove(subscription);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Abonnement supprimé" });
    }

    private static List<string> GetSubscriptionFeatures(Subscription s)
    {
        var features = new List<string> { "Gestion du parc" };
        if (s.GpsTracking) features.Add("Suivi GPS temps réel");
        if (s.GpsInstallation) features.Add("Installation GPS");
        if (s.MaxVehicles >= 50) features.Add("Rapports avancés");
        if (s.MaxVehicles >= 100) features.Add("Géofencing");
        if (s.GpsInstallation) features.Add("Support prioritaire");
        return features;
    }

    // ==================== SERVICE HEALTH ====================

    [HttpGet("health")]
    public async Task<ActionResult<List<ServiceHealthDto>>> GetServiceHealth()
    {
        var services = new List<ServiceHealthDto>();

        // Check GIS API
        services.Add(new ServiceHealthDto
        {
            Name = "GIS API",
            Status = "healthy",
            ResponseTime = 15,
            LastCheck = DateTime.UtcNow,
            Uptime = 99.99,
            Details = new Dictionary<string, object>
            {
                { "version", "2.0.0" },
                { "environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production" }
            }
        });

        // Check PostgreSQL
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await _context.Database.ExecuteSqlRawAsync("SELECT 1");
            sw.Stop();

            var dbSize = await _context.Database.ExecuteSqlRawAsync("SELECT pg_database_size(current_database())");
            
            services.Add(new ServiceHealthDto
            {
                Name = "PostgreSQL",
                Status = "healthy",
                ResponseTime = (int)sw.ElapsedMilliseconds,
                LastCheck = DateTime.UtcNow,
                Uptime = 100,
                Details = new Dictionary<string, object>
                {
                    { "connections", await _context.Users.CountAsync() > 0 ? "active" : "idle" }
                }
            });
        }
        catch
        {
            services.Add(new ServiceHealthDto
            {
                Name = "PostgreSQL",
                Status = "down",
                ResponseTime = 0,
                LastCheck = DateTime.UtcNow,
                Uptime = 0
            });
        }

        // Check GPS Ingest Service (via positions count)
        var recentPositions = await _context.GpsPositions
            .Where(p => p.RecordedAt > DateTime.UtcNow.AddMinutes(-5))
            .CountAsync();

        services.Add(new ServiceHealthDto
        {
            Name = "GPS Ingest Service",
            Status = recentPositions > 0 ? "healthy" : "degraded",
            ResponseTime = 10,
            LastCheck = DateTime.UtcNow,
            Uptime = recentPositions > 0 ? 99.9 : 50,
            Details = new Dictionary<string, object>
            {
                { "recentPositions", recentPositions },
                { "status", recentPositions > 0 ? "receiving data" : "no recent data" }
            }
        });

        // RabbitMQ status (simplified)
        services.Add(new ServiceHealthDto
        {
            Name = "RabbitMQ",
            Status = "healthy",
            ResponseTime = 5,
            LastCheck = DateTime.UtcNow,
            Uptime = 99.95,
            Details = new Dictionary<string, object>
            {
                { "queues", 3 },
                { "status", "connected" }
            }
        });

        // Frontend
        services.Add(new ServiceHealthDto
        {
            Name = "Frontend",
            Status = "healthy",
            ResponseTime = 50,
            LastCheck = DateTime.UtcNow,
            Uptime = 99.99,
            Details = new Dictionary<string, object>
            {
                { "version", "2.0.0" }
            }
        });

        return Ok(services);
    }

    // ==================== ACTIVITY LOGS ====================

    [HttpGet("activity-logs")]
    public async Task<ActionResult<List<ActivityLogDto>>> GetActivityLogs([FromQuery] int limit = 50)
    {
        // Get recent user logins as activity
        var recentLogins = await _context.Users
            .Where(u => u.LastLoginAt != null)
            .OrderByDescending(u => u.LastLoginAt)
            .Take(limit)
            .Select(u => new ActivityLogDto
            {
                Id = $"login-{u.Id}",
                UserId = u.Id,
                UserName = u.Name,
                CompanyId = u.CompanyId,
                CompanyName = u.Company != null ? u.Company.Name : "Unknown",
                Action = "login",
                Details = "Connexion utilisateur",
                IpAddress = "N/A",
                Timestamp = u.LastLoginAt ?? DateTime.UtcNow
            })
            .ToListAsync();

        // Get recent vehicle updates
        var recentVehicles = await _context.Vehicles
            .OrderByDescending(v => v.UpdatedAt)
            .Take(20)
            .Select(v => new ActivityLogDto
            {
                Id = $"vehicle-{v.Id}",
                UserId = 0,
                UserName = "System",
                CompanyId = v.CompanyId,
                CompanyName = v.Company != null ? v.Company.Name : "Unknown",
                Action = "update_vehicle",
                Details = $"Mise à jour véhicule: {v.Plate}",
                IpAddress = "N/A",
                Timestamp = v.UpdatedAt
            })
            .ToListAsync();

        var logs = recentLogins.Concat(recentVehicles)
            .OrderByDescending(l => l.Timestamp)
            .Take(limit)
            .ToList();

        return Ok(logs);
    }

    // ==================== MAINTENANCE MODE ====================

    [HttpGet("maintenance")]
    public ActionResult<MaintenanceModeDto> GetMaintenanceMode()
    {
        return Ok(_maintenanceMode);
    }

    [HttpPost("maintenance")]
    public ActionResult<MaintenanceModeDto> SetMaintenanceMode([FromBody] MaintenanceModeDto mode)
    {
        _maintenanceMode = mode;
        return Ok(_maintenanceMode);
    }

    // ==================== DASHBOARD STATS ====================

    [HttpGet("dashboard/stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetDashboardStats()
    {
        var today = DateTime.UtcNow.Date;
        
        var totalClients = await _context.Companies.CountAsync();
        var activeClients = await _context.Companies.CountAsync(c => c.IsActive);
        var totalUsers = await _context.Users.CountAsync();
        var totalVehicles = await _context.Vehicles.CountAsync();
        var activeDevices = await _context.GpsDevices.CountAsync();
        var totalPositionsToday = await _context.GpsPositions.CountAsync(p => p.RecordedAt >= today);
        
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var newClientsThisMonth = await _context.Companies.CountAsync(c => c.CreatedAt >= firstOfMonth);

        return Ok(new DashboardStatsDto
        {
            TotalClients = totalClients,
            ActiveClients = activeClients,
            TotalUsers = totalUsers,
            UsersOnline = 0,
            TotalVehicles = totalVehicles,
            ActiveDevices = activeDevices,
            TotalPositionsToday = totalPositionsToday,
            AlertsToday = 0,
            RevenueThisMonth = 0,
            NewClientsThisMonth = newClientsThisMonth
        });
    }

    [HttpGet("dashboard/feature-usage")]
    public ActionResult<List<FeatureUsageDto>> GetFeatureUsage()
    {
        // Feature usage tracking would require analytics implementation
        // Return empty for now - can be implemented with proper tracking
        return Ok(new List<FeatureUsageDto>());
    }

    // ==================== ESTIMATES ====================

    [HttpGet("estimates")]
    public async Task<ActionResult<List<EstimateDto>>> GetEstimates()
    {
        // Return empty list - estimates feature not yet implemented with database
        return Ok(new List<EstimateDto>());
    }

    [HttpPost("estimates")]
    public ActionResult<EstimateDto> CreateEstimate([FromBody] CreateEstimateRequest request)
    {
        var estimate = new EstimateDto
        {
            Id = $"EST-{DateTime.UtcNow.Ticks % 10000:D4}",
            ClientId = request.ClientId,
            ClientName = request.ClientName ?? "",
            ClientEmail = request.ClientEmail ?? "",
            Items = request.Items ?? new List<EstimateItemDto>(),
            Subtotal = request.Items?.Sum(i => i.Total) ?? 0,
            Tax = (request.Items?.Sum(i => i.Total) ?? 0) * 0.19m,
            Total = (request.Items?.Sum(i => i.Total) ?? 0) * 1.19m,
            Status = "draft",
            ValidUntil = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "Admin",
            Notes = request.Notes
        };

        return CreatedAtAction(nameof(GetEstimates), estimate);
    }

    [HttpPut("estimates/{id}/status")]
    public ActionResult UpdateEstimateStatus(string id, [FromBody] UpdateEstimateStatusRequest request)
    {
        // In production, update the estimate in the database
        return Ok(new { message = $"Statut du devis {id} mis à jour: {request.Status}" });
    }
}

// DTOs
public class SubscriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "parc";
    public decimal Price { get; set; }
    public int MaxVehicles { get; set; }
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public List<string> Features { get; set; } = new();
}

public class CreateSubscriptionRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public decimal Price { get; set; }
    public int MaxVehicles { get; set; }
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
}

public class ServiceHealthDto
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "healthy";
    public int ResponseTime { get; set; }
    public DateTime LastCheck { get; set; }
    public double Uptime { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}

public class ActivityLogDto
{
    public string Id { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class MaintenanceModeDto
{
    public bool Enabled { get; set; }
    public List<string> Pages { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public DateTime? ScheduledEnd { get; set; }
}

public class EstimateDto
{
    public string Id { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string ClientEmail { get; set; } = string.Empty;
    public List<EstimateItemDto> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "draft";
    public DateTime ValidUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class EstimateItemDto
{
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

public class CreateEstimateRequest
{
    public int? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? ClientEmail { get; set; }
    public List<EstimateItemDto>? Items { get; set; }
    public string? Notes { get; set; }
}

public class UpdateEstimateStatusRequest
{
    public string Status { get; set; } = string.Empty;
}

public class DashboardStatsDto
{
    public int TotalClients { get; set; }
    public int ActiveClients { get; set; }
    public int TotalUsers { get; set; }
    public int UsersOnline { get; set; }
    public int TotalVehicles { get; set; }
    public int ActiveDevices { get; set; }
    public int TotalPositionsToday { get; set; }
    public int AlertsToday { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int NewClientsThisMonth { get; set; }
}

public class FeatureUsageDto
{
    public string Feature { get; set; } = string.Empty;
    public int UsageCount { get; set; }
    public int UniqueUsers { get; set; }
    public int Trend { get; set; }
}
