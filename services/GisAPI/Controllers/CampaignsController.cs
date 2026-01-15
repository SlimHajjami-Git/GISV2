using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Controllers;

/// <summary>
/// Campaign management endpoints - Admin only access
/// Handles CRUD operations for marketing/sales campaigns with subscription management
/// </summary>
[ApiController]
[Route("api/admin/campaigns")]
[Authorize(Roles = "admin,super_admin")]
public class CampaignsController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(GisDbContext context, ILogger<CampaignsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int? GetUserId() => int.TryParse(User.FindFirst("userId")?.Value, out var id) ? id : null;

    // ==================== CAMPAIGN CRUD ====================

    /// <summary>
    /// Get all campaigns with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CampaignDto>>> GetCampaigns(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? type)
    {
        var query = _context.Campaigns
            .Include(c => c.TargetSubscription)
            .Include(c => c.CreatedBy)
            .Include(c => c.Companies)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(c => c.Name.Contains(search) || 
                (c.Description != null && c.Description.Contains(search)));
        }

        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            query = query.Where(c => c.Status == status);
        }

        if (!string.IsNullOrEmpty(type) && type != "all")
        {
            query = query.Where(c => c.Type == type);
        }

        var campaigns = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(campaigns.Select(MapToCampaignDto));
    }

    /// <summary>
    /// Get a specific campaign by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<CampaignDto>> GetCampaign(int id)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.TargetSubscription)
            .Include(c => c.CreatedBy)
            .Include(c => c.Companies)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            return NotFound(new { message = "Campagne introuvable" });

        return Ok(MapToCampaignDto(campaign));
    }

    /// <summary>
    /// Get campaigns for a specific client/company
    /// </summary>
    [HttpGet("client/{clientId}")]
    public async Task<ActionResult<List<CampaignDto>>> GetCampaignsByClient(int clientId)
    {
        var company = await _context.Companies
            .Include(c => c.Campaign)
            .ThenInclude(camp => camp!.TargetSubscription)
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (company == null)
            return NotFound(new { message = "Client introuvable" });

        // Get the company's current campaign and available active campaigns
        var activeCampaigns = await _context.Campaigns
            .Include(c => c.TargetSubscription)
            .Where(c => c.IsActive && c.Status == "active")
            .Where(c => !c.MaxSubscriptions.HasValue || c.CurrentSubscriptions < c.MaxSubscriptions)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(activeCampaigns.Select(MapToCampaignDto));
    }

    /// <summary>
    /// Create a new campaign - Admin only
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CampaignDto>> CreateCampaign([FromBody] CreateCampaignRequest request)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Le nom de la campagne est requis" });

        if (request.DiscountPercentage.HasValue && (request.DiscountPercentage < 0 || request.DiscountPercentage > 100))
            return BadRequest(new { message = "Le pourcentage de remise doit être entre 0 et 100" });

        if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate > request.EndDate)
            return BadRequest(new { message = "La date de fin doit être après la date de début" });

        // Verify target subscription exists if provided
        if (request.TargetSubscriptionId.HasValue)
        {
            var subscription = await _context.Subscriptions.FindAsync(request.TargetSubscriptionId.Value);
            if (subscription == null)
                return BadRequest(new { message = "Abonnement cible introuvable" });
        }

        var campaign = new Campaign
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type ?? "standard",
            Status = request.Status ?? "draft",
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            DiscountPercentage = request.DiscountPercentage,
            DiscountAmount = request.DiscountAmount,
            MaxSubscriptions = request.MaxSubscriptions,
            TargetSubscriptionId = request.TargetSubscriptionId,
            AccessRights = request.AccessRights ?? GetDefaultAccessRights(request.Type ?? "standard"),
            IsActive = request.IsActive ?? true,
            CreatedById = GetUserId()
        };

        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Campaign created: {CampaignId} - {CampaignName}", campaign.Id, campaign.Name);

        // Reload with includes
        await _context.Entry(campaign).Reference(c => c.TargetSubscription).LoadAsync();
        await _context.Entry(campaign).Reference(c => c.CreatedBy).LoadAsync();

        return CreatedAtAction(nameof(GetCampaign), new { id = campaign.Id }, MapToCampaignDto(campaign));
    }

    /// <summary>
    /// Update an existing campaign - Admin only
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<CampaignDto>> UpdateCampaign(int id, [FromBody] UpdateCampaignRequest request)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.TargetSubscription)
            .Include(c => c.CreatedBy)
            .Include(c => c.Companies)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            return NotFound(new { message = "Campagne introuvable" });

        // Validation
        if (request.DiscountPercentage.HasValue && (request.DiscountPercentage < 0 || request.DiscountPercentage > 100))
            return BadRequest(new { message = "Le pourcentage de remise doit être entre 0 et 100" });

        var startDate = request.StartDate ?? campaign.StartDate;
        var endDate = request.EndDate ?? campaign.EndDate;
        if (startDate.HasValue && endDate.HasValue && startDate > endDate)
            return BadRequest(new { message = "La date de fin doit être après la date de début" });

        // Update fields
        if (!string.IsNullOrWhiteSpace(request.Name)) campaign.Name = request.Name;
        if (request.Description != null) campaign.Description = request.Description;
        if (!string.IsNullOrWhiteSpace(request.Type)) campaign.Type = request.Type;
        if (!string.IsNullOrWhiteSpace(request.Status)) campaign.Status = request.Status;
        if (request.StartDate.HasValue) campaign.StartDate = request.StartDate;
        if (request.EndDate.HasValue) campaign.EndDate = request.EndDate;
        if (request.DiscountPercentage.HasValue) campaign.DiscountPercentage = request.DiscountPercentage;
        if (request.DiscountAmount.HasValue) campaign.DiscountAmount = request.DiscountAmount;
        if (request.MaxSubscriptions.HasValue) campaign.MaxSubscriptions = request.MaxSubscriptions;
        if (request.TargetSubscriptionId.HasValue) campaign.TargetSubscriptionId = request.TargetSubscriptionId;
        if (request.AccessRights != null) campaign.AccessRights = request.AccessRights;
        if (request.IsActive.HasValue) campaign.IsActive = request.IsActive.Value;

        campaign.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Campaign updated: {CampaignId} - {CampaignName}", campaign.Id, campaign.Name);

        return Ok(MapToCampaignDto(campaign));
    }

    /// <summary>
    /// Delete a campaign - Admin only
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCampaign(int id)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Companies)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
            return NotFound(new { message = "Campagne introuvable" });

        // Check if companies are enrolled
        if (campaign.Companies.Any())
        {
            return BadRequest(new { 
                message = $"Impossible de supprimer: {campaign.Companies.Count} société(s) sont inscrites à cette campagne" 
            });
        }

        _context.Campaigns.Remove(campaign);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Campaign deleted: {CampaignId} - {CampaignName}", campaign.Id, campaign.Name);

        return Ok(new { message = "Campagne supprimée avec succès" });
    }

    // ==================== CAMPAIGN ENROLLMENT ====================

    /// <summary>
    /// Enroll a company in a campaign
    /// </summary>
    [HttpPost("{campaignId}/enroll/{companyId}")]
    public async Task<ActionResult> EnrollCompany(int campaignId, int companyId)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Companies)
            .FirstOrDefaultAsync(c => c.Id == campaignId);

        if (campaign == null)
            return NotFound(new { message = "Campagne introuvable" });

        if (!campaign.IsActive || campaign.Status != "active")
            return BadRequest(new { message = "Cette campagne n'est pas active" });

        if (campaign.MaxSubscriptions.HasValue && campaign.CurrentSubscriptions >= campaign.MaxSubscriptions)
            return BadRequest(new { message = "Cette campagne a atteint son nombre maximum d'inscriptions" });

        if (campaign.EndDate.HasValue && campaign.EndDate < DateTime.UtcNow)
            return BadRequest(new { message = "Cette campagne est terminée" });

        var company = await _context.Companies.FindAsync(companyId);
        if (company == null)
            return NotFound(new { message = "Société introuvable" });

        if (company.CampaignId == campaignId)
            return BadRequest(new { message = "Cette société est déjà inscrite à cette campagne" });

        // Enroll company
        company.CampaignId = campaignId;
        company.UpdatedAt = DateTime.UtcNow;
        campaign.CurrentSubscriptions++;
        campaign.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Company {CompanyId} enrolled in campaign {CampaignId}", companyId, campaignId);

        return Ok(new { message = "Société inscrite à la campagne avec succès" });
    }

    /// <summary>
    /// Remove a company from a campaign
    /// </summary>
    [HttpPost("{campaignId}/unenroll/{companyId}")]
    public async Task<ActionResult> UnenrollCompany(int campaignId, int companyId)
    {
        var company = await _context.Companies.FindAsync(companyId);
        if (company == null)
            return NotFound(new { message = "Société introuvable" });

        if (company.CampaignId != campaignId)
            return BadRequest(new { message = "Cette société n'est pas inscrite à cette campagne" });

        var campaign = await _context.Campaigns.FindAsync(campaignId);
        if (campaign != null && campaign.CurrentSubscriptions > 0)
        {
            campaign.CurrentSubscriptions--;
            campaign.UpdatedAt = DateTime.UtcNow;
        }

        company.CampaignId = null;
        company.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Company {CompanyId} unenrolled from campaign {CampaignId}", companyId, campaignId);

        return Ok(new { message = "Société désinscrite de la campagne avec succès" });
    }

    // ==================== HELPER METHODS ====================

    private static CampaignDto MapToCampaignDto(Campaign campaign)
    {
        return new CampaignDto
        {
            Id = campaign.Id,
            Name = campaign.Name,
            Description = campaign.Description,
            Type = campaign.Type,
            Status = campaign.Status,
            StartDate = campaign.StartDate,
            EndDate = campaign.EndDate,
            DiscountPercentage = campaign.DiscountPercentage,
            DiscountAmount = campaign.DiscountAmount,
            MaxSubscriptions = campaign.MaxSubscriptions,
            CurrentSubscriptions = campaign.CurrentSubscriptions,
            TargetSubscriptionId = campaign.TargetSubscriptionId,
            TargetSubscriptionName = campaign.TargetSubscription?.Name,
            AccessRights = campaign.AccessRights,
            IsActive = campaign.IsActive,
            CreatedAt = campaign.CreatedAt,
            UpdatedAt = campaign.UpdatedAt,
            CreatedByName = campaign.CreatedBy?.Name,
            EnrolledCompanies = campaign.Companies?.Count ?? 0
        };
    }

    private static CampaignAccessRights GetDefaultAccessRights(string campaignType)
    {
        return campaignType switch
        {
            "trial" => new CampaignAccessRights
            {
                Dashboard = true, Monitoring = true, Vehicles = true, Employees = true,
                GpsDevices = true, Maintenance = false, Costs = false, Reports = false,
                Geofences = false, Notifications = true, Settings = true, Users = true,
                MaxVehicles = 5, MaxUsers = 2, MaxGpsDevices = 5, MaxGeofences = 5,
                HistoryRetentionDays = 7
            },
            "promotional" => new CampaignAccessRights
            {
                Dashboard = true, Monitoring = true, Vehicles = true, Employees = true,
                GpsDevices = true, Maintenance = true, Costs = true, Reports = true,
                Geofences = true, Notifications = true, Settings = true, Users = true,
                MaxVehicles = 20, MaxUsers = 10, MaxGpsDevices = 20, MaxGeofences = 30,
                HistoryRetentionDays = 90, HistoryPlayback = true, RealTimeAlerts = true
            },
            "enterprise" => new CampaignAccessRights
            {
                Dashboard = true, Monitoring = true, Vehicles = true, Employees = true,
                GpsDevices = true, Maintenance = true, Costs = true, Reports = true,
                Geofences = true, Notifications = true, Settings = true, Users = true,
                ApiAccess = true, CustomBranding = true, AdvancedReports = true,
                RealTimeAlerts = true, HistoryPlayback = true, FuelAnalysis = true,
                DrivingBehavior = true, MultiCompany = true,
                MaxVehicles = 500, MaxUsers = 100, MaxGpsDevices = 500, MaxGeofences = 200,
                HistoryRetentionDays = 365
            },
            _ => new CampaignAccessRights() // standard defaults
        };
    }
}

// ==================== DTOs ====================

public class CampaignDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? MaxSubscriptions { get; set; }
    public int CurrentSubscriptions { get; set; }
    public int? TargetSubscriptionId { get; set; }
    public string? TargetSubscriptionName { get; set; }
    public CampaignAccessRights? AccessRights { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedByName { get; set; }
    public int EnrolledCompanies { get; set; }
}

public class CreateCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? MaxSubscriptions { get; set; }
    public int? TargetSubscriptionId { get; set; }
    public CampaignAccessRights? AccessRights { get; set; }
    public bool? IsActive { get; set; }
}

public class UpdateCampaignRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Type { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public int? MaxSubscriptions { get; set; }
    public int? TargetSubscriptionId { get; set; }
    public CampaignAccessRights? AccessRights { get; set; }
    public bool? IsActive { get; set; }
}

