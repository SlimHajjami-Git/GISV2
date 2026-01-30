using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/companies")]
[Authorize]
public class CompanyController : ControllerBase
{
    private readonly GisDbContext _context;

    public CompanyController(GisDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetCompanies(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        var query = _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Users)
            .Include(c => c.Vehicles)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(c => 
                c.Name.ToLower().Contains(search.ToLower()) ||
                (c.Email != null && c.Email.ToLower().Contains(search.ToLower())));
        }

        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            if (status == "active")
                query = query.Where(c => c.IsActive);
            else if (status == "suspended")
                query = query.Where(c => !c.IsActive);
        }

        var companies = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CompanyDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                Phone = c.Phone,
                Type = c.Type,
                Address = c.Address,
                City = c.City,
                Country = c.Country,
                TaxId = c.TaxId,
                LogoUrl = c.LogoUrl,
                IsActive = c.IsActive,
                Status = c.IsActive ? "active" : "suspended",
                SubscriptionId = c.SubscriptionTypeId ?? 0,
                SubscriptionName = c.SubscriptionType != null ? c.SubscriptionType.Name : null,
                MaxVehicles = c.SubscriptionType != null ? c.SubscriptionType.MaxVehicles : 0,
                CurrentVehicles = c.Vehicles.Count,
                CurrentUsers = c.Users.Count,
                SubscriptionExpiresAt = c.SubscriptionExpiresAt,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();

        return Ok(companies);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CompanyDto>> GetCompany(int id)
    {
        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Users)
            .Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        return Ok(new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            Address = company.Address,
            City = company.City,
            Country = company.Country,
            TaxId = company.TaxId,
            LogoUrl = company.LogoUrl,
            IsActive = company.IsActive,
            Status = company.IsActive ? "active" : "suspended",
            SubscriptionId = company.SubscriptionTypeId ?? 0,
            SubscriptionName = company.SubscriptionType?.Name,
            MaxVehicles = company.SubscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = company.Vehicles.Count,
            CurrentUsers = company.Users.Count,
            SubscriptionExpiresAt = company.SubscriptionExpiresAt,
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<ActionResult<CompanyDto>> CreateCompany([FromBody] CreateCompanyRequest request)
    {
        if (await _context.Societes.AnyAsync(c => c.Email == request.Email))
        {
            return BadRequest(new { message = "Une société avec cet email existe déjà" });
        }

        var subscriptionType = await _context.SubscriptionTypes.FindAsync(request.SubscriptionId);
        if (subscriptionType == null && request.SubscriptionId > 0)
        {
            return BadRequest(new { message = "Abonnement non trouvé" });
        }

        var company = new Societe
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Type = request.Type ?? "transport",
            Address = request.Address,
            City = request.City,
            Country = request.Country ?? "TN",
            TaxId = request.TaxId,
            SubscriptionTypeId = request.SubscriptionId > 0 ? request.SubscriptionId : null,
            IsActive = true,
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1),
            Settings = new SocieteSettings
            {
                Currency = "DT",
                Timezone = "Africa/Tunis",
                Language = "fr"
            }
        };

        _context.Societes.Add(company);
        await _context.SaveChangesAsync();

        // Create default admin user for the company if requested
        if (!string.IsNullOrEmpty(request.AdminEmail) && !string.IsNullOrEmpty(request.AdminPassword))
        {
            var adminUser = new User
            {
                Name = request.AdminName ?? request.Name + " Admin",
                Email = request.AdminEmail,
                Phone = request.Phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword),
                Roles = new[] { "admin" },
                Permissions = new[] { "dashboard", "monitoring", "vehicles", "employees", "maintenance", "costs", "reports", "geofences", "notifications", "settings", "users" },
                CompanyId = company.Id,
                Status = "active"
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetCompany), new { id = company.Id }, new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            IsActive = company.IsActive,
            Status = "active",
            SubscriptionId = company.SubscriptionTypeId ?? 0,
            SubscriptionName = subscriptionType?.Name,
            MaxVehicles = subscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = 0,
            CurrentUsers = !string.IsNullOrEmpty(request.AdminEmail) ? 1 : 0,
            CreatedAt = company.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CompanyDto>> UpdateCompany(int id, [FromBody] UpdateCompanyRequest request)
    {
        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        if (!string.IsNullOrEmpty(request.Email) && request.Email != company.Email)
        {
            if (await _context.Societes.AnyAsync(c => c.Email == request.Email && c.Id != id))
            {
                return BadRequest(new { message = "Une société avec cet email existe déjà" });
            }
        }

        if (!string.IsNullOrEmpty(request.Name)) company.Name = request.Name;
        if (request.Email != null) company.Email = request.Email;
        if (request.Phone != null) company.Phone = request.Phone;
        if (!string.IsNullOrEmpty(request.Type)) company.Type = request.Type;
        if (request.Address != null) company.Address = request.Address;
        if (request.City != null) company.City = request.City;
        if (!string.IsNullOrEmpty(request.Country)) company.Country = request.Country;
        if (request.TaxId != null) company.TaxId = request.TaxId;
        if (request.SubscriptionId.HasValue && request.SubscriptionId > 0)
        {
            company.SubscriptionTypeId = request.SubscriptionId.Value;
        }

        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var subscriptionType = await _context.SubscriptionTypes.FindAsync(company.SubscriptionTypeId);

        return Ok(new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            Address = company.Address,
            City = company.City,
            Country = company.Country,
            TaxId = company.TaxId,
            IsActive = company.IsActive,
            Status = company.IsActive ? "active" : "suspended",
            SubscriptionId = company.SubscriptionTypeId ?? 0,
            SubscriptionName = subscriptionType?.Name,
            MaxVehicles = subscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = company.Vehicles.Count,
            CurrentUsers = company.Users.Count,
            CreatedAt = company.CreatedAt,
            UpdatedAt = company.UpdatedAt
        });
    }

    [HttpPost("{id}/suspend")]
    public async Task<ActionResult> SuspendCompany(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null)
            return NotFound();

        company.IsActive = false;
        company.UpdatedAt = DateTime.UtcNow;

        // Suspend all users of this company
        var users = await _context.Users.Where(u => u.CompanyId == id).ToListAsync();
        foreach (var user in users)
        {
            user.Status = "suspended";
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Société suspendue avec succès" });
    }

    [HttpPost("{id}/activate")]
    public async Task<ActionResult> ActivateCompany(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null)
            return NotFound();

        company.IsActive = true;
        company.UpdatedAt = DateTime.UtcNow;

        // Reactivate all users of this company
        var users = await _context.Users.Where(u => u.CompanyId == id).ToListAsync();
        foreach (var user in users)
        {
            user.Status = "active";
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Société activée avec succès" });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteCompany(int id)
    {
        var company = await _context.Societes
            .Include(c => c.Users)
            .Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        if (company.Users.Any() || company.Vehicles.Any())
        {
            return BadRequest(new { message = "Impossible de supprimer une société avec des utilisateurs ou véhicules actifs. Veuillez d'abord les supprimer ou les transférer." });
        }

        _context.Societes.Remove(company);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Société supprimée avec succès" });
    }

    [HttpGet("{id}/users")]
    public async Task<ActionResult<List<CompanyUserDto>>> GetCompanyUsers(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null)
            return NotFound();

        var users = await _context.Users
            .Where(u => u.CompanyId == id)
            .Select(u => new CompanyUserDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Phone = u.Phone,
                Roles = u.Roles,
                Permissions = u.Permissions,
                Status = u.Status,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}/stats")]
    public async Task<ActionResult<CompanyStatsDto>> GetCompanyStats(int id)
    {
        var company = await _context.Societes
            .Include(c => c.Users)
            .Include(c => c.Vehicles)
            .Include(c => c.GpsDevices)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        var today = DateTime.UtcNow.Date;
        var positionsToday = await _context.GpsPositions
            .Where(p => company.GpsDevices.Select(d => d.Id).Contains(p.DeviceId) && p.RecordedAt >= today)
            .CountAsync();

        return Ok(new CompanyStatsDto
        {
            TotalUsers = company.Users.Count,
            ActiveUsers = company.Users.Count(u => u.Status == "active"),
            TotalVehicles = company.Vehicles.Count,
            TotalDevices = company.GpsDevices.Count,
            PositionsToday = positionsToday
        });
    }
}

// DTOs
public class CompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Type { get; set; } = "transport";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TaxId { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsActive { get; set; }
    public string Status { get; set; } = "active";
    public int SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }
    public int MaxVehicles { get; set; }
    public int CurrentVehicles { get; set; }
    public int CurrentUsers { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateCompanyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Type { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TaxId { get; set; }
    public int SubscriptionId { get; set; }
    
    // Optional: Create admin user for the company
    public string? AdminName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

public class UpdateCompanyRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Type { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? TaxId { get; set; }
    public int? SubscriptionId { get; set; }
}

public class CompanyUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CompanyStatsDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int TotalVehicles { get; set; }
    public int TotalDevices { get; set; }
    public int PositionsToday { get; set; }
}
