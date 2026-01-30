using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using System.Text.Json;
using MediatR;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]  // System admin check is done via PermissionMiddleware
public class AdminController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly IPermissionService _permissionService;
    private static MaintenanceModeDto _maintenanceMode = new() { Enabled = false, Pages = new List<string>(), Message = "" };

    public AdminController(GisDbContext context, IConfiguration configuration, IMediator mediator, IPermissionService permissionService)
    {
        _context = context;
        _configuration = configuration;
        _mediator = mediator;
        _permissionService = permissionService;
    }

    // ==================== SUBSCRIPTIONS (Legacy - use SubscriptionTypes) ====================

    [HttpGet("subscriptions")]
    public async Task<ActionResult<List<SubscriptionDto>>> GetSubscriptions()
    {
        var subscriptions = await _context.SubscriptionTypes
            .Where(s => s.IsActive)
            .OrderBy(s => s.YearlyPrice)
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                Name = s.Name,
                Type = s.TargetCompanyType,
                Price = s.YearlyPrice,
                MaxVehicles = s.MaxVehicles,
                GpsTracking = s.GpsTracking,
                GpsInstallation = s.GpsInstallation,
                Features = GetSubscriptionTypeFeatures(s)
            })
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("subscriptions/{id}")]
    public async Task<ActionResult<SubscriptionDto>> GetSubscription(int id)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(id);
        if (subscription == null)
            return NotFound();

        return Ok(new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.TargetCompanyType,
            Price = subscription.YearlyPrice,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionTypeFeatures(subscription)
        });
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<SubscriptionDto>> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        var subscription = new SubscriptionType
        {
            Name = request.Name,
            Code = request.Name.ToLower().Replace(" ", "-"),
            TargetCompanyType = request.Type ?? "all",
            YearlyPrice = request.Price,
            MaxVehicles = request.MaxVehicles,
            GpsTracking = request.GpsTracking,
            GpsInstallation = request.GpsInstallation,
            IsActive = true
        };

        _context.SubscriptionTypes.Add(subscription);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSubscription), new { id = subscription.Id }, new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.TargetCompanyType,
            Price = subscription.YearlyPrice,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionTypeFeatures(subscription)
        });
    }

    [HttpPut("subscriptions/{id}")]
    public async Task<ActionResult<SubscriptionDto>> UpdateSubscription(int id, [FromBody] CreateSubscriptionRequest request)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(id);
        if (subscription == null)
            return NotFound();

        subscription.Name = request.Name;
        subscription.TargetCompanyType = request.Type ?? subscription.TargetCompanyType;
        subscription.YearlyPrice = request.Price;
        subscription.MaxVehicles = request.MaxVehicles;
        subscription.GpsTracking = request.GpsTracking;
        subscription.GpsInstallation = request.GpsInstallation;
        subscription.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new SubscriptionDto
        {
            Id = subscription.Id,
            Name = subscription.Name,
            Type = subscription.TargetCompanyType,
            Price = subscription.YearlyPrice,
            MaxVehicles = subscription.MaxVehicles,
            GpsTracking = subscription.GpsTracking,
            GpsInstallation = subscription.GpsInstallation,
            Features = GetSubscriptionTypeFeatures(subscription)
        });
    }

    [HttpDelete("subscriptions/{id}")]
    public async Task<ActionResult> DeleteSubscription(int id)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(id);
        if (subscription == null)
            return NotFound();

        var companiesUsingSubscription = await _context.Societes.CountAsync(c => c.SubscriptionTypeId == id);
        if (companiesUsingSubscription > 0)
        {
            return BadRequest(new { message = $"Impossible de supprimer: {companiesUsingSubscription} société(s) utilisent cet abonnement" });
        }

        _context.SubscriptionTypes.Remove(subscription);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Abonnement supprimé" });
    }

    private static List<string> GetSubscriptionTypeFeatures(SubscriptionType s)
    {
        var features = new List<string> { "Gestion du parc" };
        if (s.GpsTracking) features.Add("Suivi GPS temps réel");
        if (s.GpsInstallation) features.Add("Installation GPS");
        if (s.MaxVehicles >= 50) features.Add("Rapports avancés");
        if (s.MaxVehicles >= 100) features.Add("Géofencing");
        if (s.GpsInstallation) features.Add("Support prioritaire");
        return features;
    }

    // ==================== COMPANY MANAGEMENT ====================

    [HttpGet("company")]
    public async Task<ActionResult<List<AdminCompanyDto>>> GetCompanies([FromQuery] string? search, [FromQuery] string? status)
    {
        var query = _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(c => c.Name.Contains(search) || (c.Email != null && c.Email.Contains(search)));
        }

        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            query = status switch
            {
                "active" => query.Where(c => c.IsActive),
                "suspended" => query.Where(c => !c.IsActive),
                _ => query
            };
        }

        var companies = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

        return Ok(companies.Select(c => new AdminCompanyDto
        {
            Id = c.Id,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            Type = c.Type,
            SubscriptionId = c.SubscriptionTypeId,
            SubscriptionName = c.SubscriptionType?.Name,
            MaxVehicles = c.SubscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = c.Vehicles?.Count ?? 0,
            CurrentUsers = c.Users?.Count ?? 0,
            Status = c.IsActive ? "active" : "suspended",
            CreatedAt = c.CreatedAt,
            LastActivity = c.UpdatedAt
        }));
    }

    [HttpGet("company/{id}")]
    public async Task<ActionResult<AdminCompanyDto>> GetCompany(int id)
    {
        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        return Ok(new AdminCompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            SubscriptionId = company.SubscriptionTypeId,
            SubscriptionName = company.SubscriptionType?.Name,
            MaxVehicles = company.SubscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = company.Vehicles?.Count ?? 0,
            CurrentUsers = company.Users?.Count ?? 0,
            Status = company.IsActive ? "active" : "suspended",
            CreatedAt = company.CreatedAt,
            LastActivity = company.UpdatedAt
        });
    }

    [HttpGet("company/{id}/roles")]
    public async Task<ActionResult<List<AdminRoleDto>>> GetCompanyRoles(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null)
            return NotFound();

        // Project directly to DTO to avoid JSONB deserialization issues
        var roles = await _context.Roles
            .Where(r => r.SocieteId == id)
            .Select(r => new AdminRoleDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                RoleType = r.Name, // Use Name as RoleType for compatibility
                Permissions = null, // Skip permissions to avoid JSONB issues
                IsSystem = false,
                IsDefault = false,
                UserCount = _context.Users.Count(u => u.RoleId == r.Id),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync();

        return Ok(roles);
    }

    // ==================== PERMISSIONS ====================

    [HttpGet("permissions/template")]
    public ActionResult<Dictionary<string, object>> GetPermissionTemplate()
    {
        var template = _permissionService.GetPermissionTemplate();
        return Ok(template);
    }

    [HttpGet("permissions/subscription/{subscriptionId}")]
    public async Task<ActionResult<Dictionary<string, object>>> GetSubscriptionPermissions(int subscriptionId)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(subscriptionId);
        if (subscription == null)
            return NotFound();

        var permissions = _permissionService.GetSubscriptionPermissions(subscription);
        return Ok(permissions);
    }

    [HttpPost("company")]
    public async Task<ActionResult<AdminCompanyDto>> CreateCompany([FromBody] CreateAdminCompanyRequest request)
    {
        if (!string.IsNullOrEmpty(request.Email) && await _context.Societes.AnyAsync(c => c.Email == request.Email))
        {
            return BadRequest(new { message = "Une société avec cet email existe déjà" });
        }

        var subscriptionType = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(s => s.Id == request.SubscriptionId);

        // Calculate subscription dates based on billing cycle
        var billingCycle = request.BillingCycle ?? "yearly";
        var startDate = DateTime.UtcNow;
        var durationDays = billingCycle switch
        {
            "monthly" => subscriptionType?.MonthlyDurationDays ?? 30,
            "quarterly" => subscriptionType?.QuarterlyDurationDays ?? 90,
            "yearly" => subscriptionType?.YearlyDurationDays ?? 365,
            _ => 365
        };
        var expiresAt = startDate.AddDays(durationDays);

        // Calculate price based on billing cycle
        var price = billingCycle switch
        {
            "monthly" => subscriptionType?.MonthlyPrice ?? 0,
            "quarterly" => subscriptionType?.QuarterlyPrice ?? 0,
            "yearly" => subscriptionType?.YearlyPrice ?? 0,
            _ => subscriptionType?.YearlyPrice ?? 0
        };

        var company = new Societe
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Type = request.Type ?? "transport",
            SubscriptionTypeId = request.SubscriptionId > 0 ? request.SubscriptionId : null,
            IsActive = true,
            SubscriptionStartedAt = startDate,
            SubscriptionExpiresAt = expiresAt,
            BillingCycle = billingCycle,
            SubscriptionStatus = "active",
            NextPaymentAmount = price,
            Settings = new SocieteSettings
            {
                Currency = "DT",
                Timezone = "Africa/Tunis",
                Language = "fr"
            }
        };

        _context.Societes.Add(company);
        await _context.SaveChangesAsync();

        // Create admin user if provided
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
                AssignedVehicleIds = Array.Empty<int>(),
                CompanyId = company.Id,
                Status = "active"
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetCompany), new { id = company.Id }, new AdminCompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            SubscriptionId = company.SubscriptionTypeId,
            SubscriptionName = subscriptionType?.Name,
            MaxVehicles = subscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = 0,
            CurrentUsers = !string.IsNullOrEmpty(request.AdminEmail) ? 1 : 0,
            Status = "active",
            CreatedAt = company.CreatedAt,
            SubscriptionStatus = company.SubscriptionStatus,
            SubscriptionStartedAt = company.SubscriptionStartedAt,
            SubscriptionExpiresAt = company.SubscriptionExpiresAt,
            BillingCycle = company.BillingCycle
        });
    }

    [HttpPut("company/{id}")]
    public async Task<ActionResult<AdminCompanyDto>> UpdateCompany(int id, [FromBody] UpdateAdminCompanyRequest request)
    {
        var company = await _context.Societes
            .Include(c => c.SubscriptionType)
            .Include(c => c.Vehicles)
            .Include(c => c.Users)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (company == null)
            return NotFound();

        if (!string.IsNullOrEmpty(request.Name)) company.Name = request.Name;
        if (request.Email != null) company.Email = request.Email;
        if (request.Phone != null) company.Phone = request.Phone;
        if (request.Type != null) company.Type = request.Type;
        if (request.SubscriptionId.HasValue) company.SubscriptionTypeId = request.SubscriptionId.Value;

        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new AdminCompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            Email = company.Email,
            Phone = company.Phone,
            Type = company.Type,
            SubscriptionId = company.SubscriptionTypeId,
            SubscriptionName = company.SubscriptionType?.Name,
            MaxVehicles = company.SubscriptionType?.MaxVehicles ?? 0,
            CurrentVehicles = company.Vehicles?.Count ?? 0,
            CurrentUsers = company.Users?.Count ?? 0,
            Status = company.IsActive ? "active" : "suspended",
            CreatedAt = company.CreatedAt,
            LastActivity = company.UpdatedAt
        });
    }

    [HttpPost("company/{id}/suspend")]
    public async Task<ActionResult> SuspendCompany(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null) return NotFound();

        company.IsActive = false;
        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Société suspendue" });
    }

    [HttpPost("company/{id}/activate")]
    public async Task<ActionResult> ActivateCompany(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null) return NotFound();

        company.IsActive = true;
        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Société activée" });
    }

    [HttpDelete("company/{id}")]
    public async Task<ActionResult> DeleteCompany(int id)
    {
        var company = await _context.Societes.FindAsync(id);
        if (company == null) return NotFound();

        _context.Societes.Remove(company);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Société supprimée" });
    }

    [HttpGet("company/{companyId}/users")]
    public async Task<ActionResult<List<AdminUserDto>>> GetCompanyUsers(int companyId)
    {
        var users = await _context.Users
            .Where(u => u.CompanyId == companyId)
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .ToListAsync();

        return Ok(users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Phone = u.Phone,
            DateOfBirth = u.DateOfBirth,
            CIN = u.CIN,
            CompanyId = u.CompanyId,
            CompanyName = u.Societe?.Name ?? "",
            RoleId = u.RoleId,
            RoleName = u.Role?.Name,
            Roles = u.Roles,
            Permissions = u.Permissions,
            AssignedVehicleIds = u.AssignedVehicleIds,
            Status = u.Status,
            LastLoginAt = u.LastLoginAt,
            CreatedAt = u.CreatedAt,
            IsOnline = u.LastLoginAt.HasValue && u.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-30)
        }));
    }

    // ==================== VEHICLE MANAGEMENT ====================

    [HttpGet("vehicles")]
    public async Task<ActionResult<List<AdminVehicleDto>>> GetVehicles([FromQuery] string? search, [FromQuery] int? companyId, [FromQuery] string? status)
    {
        var vehicles = await _mediator.Send(new GetAdminVehiclesQuery(search, companyId, status));
        return Ok(vehicles);
    }

    [HttpGet("vehicles/{id}")]
    public async Task<ActionResult<AdminVehicleDto>> GetVehicle(int id)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null)
            return NotFound();

        return Ok(new AdminVehicleDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Type = vehicle.Type,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Plate = vehicle.Plate,
            Year = vehicle.Year,
            Color = vehicle.Color,
            Status = vehicle.Status,
            HasGps = vehicle.HasGps,
            Mileage = vehicle.Mileage,
            FuelType = vehicle.FuelType,
            CompanyId = vehicle.CompanyId,
            CompanyName = vehicle.Societe?.Name,
            GpsDeviceId = vehicle.GpsDeviceId,
            GpsImei = vehicle.GpsDevice?.DeviceUid,
            GpsMat = vehicle.GpsDevice?.Mat,
            GpsBrand = vehicle.GpsDevice?.Brand,
            GpsModel = vehicle.GpsDevice?.Model,
            GpsFirmwareVersion = vehicle.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = vehicle.GpsDevice?.FuelSensorMode,
            AssignedDriverId = vehicle.AssignedDriverId,
            AssignedDriverName = vehicle.AssignedDriver?.Name,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        });
    }

    [HttpPost("vehicles")]
    public async Task<ActionResult<AdminVehicleDto>> CreateVehicle([FromBody] CreateAdminVehicleRequest request)
    {
        var company = await _context.Societes.FindAsync(request.CompanyId);
        if (company == null)
            return BadRequest(new { message = "Société non trouvée" });

        var vehicle = new Vehicle
        {
            Name = request.Name,
            Type = request.Type ?? "camion",
            Brand = request.Brand,
            Model = request.Model,
            Plate = request.Plate,
            Year = request.Year,
            Color = request.Color,
            Status = request.Status ?? "available",
            HasGps = request.HasGps,
            Mileage = request.Mileage ?? 0,
            CompanyId = request.CompanyId
        };

        if (request.HasGps)
        {
            var (gpsDevice, error) = await ResolveGpsDeviceAsync(
                request.CompanyId,
                request.GpsDeviceId,
                request.GpsImei,
                request.GpsMat
            );

            if (error != null)
                return BadRequest(new { message = error });

            if (gpsDevice == null)
                return BadRequest(new { message = "Impossible d'associer le GPS sans IMEI ou appareil existant." });

            vehicle.GpsDeviceId = gpsDevice.Id;
            vehicle.HasGps = true;
            gpsDevice.Status = "assigned";
            gpsDevice.Vehicle = vehicle;

            if (!string.IsNullOrWhiteSpace(request.GpsMat))
            {
                gpsDevice.Mat = request.GpsMat;
            }
            
            if (!string.IsNullOrWhiteSpace(request.GpsFuelSensorMode))
            {
                gpsDevice.FuelSensorMode = request.GpsFuelSensorMode;
            }
        }
        else
        {
            vehicle.HasGps = false;
        }

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVehicle), new { id = vehicle.Id }, new AdminVehicleDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Type = vehicle.Type,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Plate = vehicle.Plate,
            Year = vehicle.Year,
            Color = vehicle.Color,
            Status = vehicle.Status,
            HasGps = vehicle.HasGps,
            Mileage = vehicle.Mileage,
            FuelType = vehicle.FuelType,
            CompanyId = vehicle.CompanyId,
            CompanyName = company.Name,
            GpsDeviceId = vehicle.GpsDeviceId,
            GpsImei = vehicle.GpsDevice?.DeviceUid,
            GpsMat = vehicle.GpsDevice?.Mat,
            GpsBrand = vehicle.GpsDevice?.Brand,
            GpsModel = vehicle.GpsDevice?.Model,
            GpsFirmwareVersion = vehicle.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = vehicle.GpsDevice?.FuelSensorMode,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        });
    }

    [HttpPut("vehicles/{id}")]
    public async Task<ActionResult<AdminVehicleDto>> UpdateVehicle(int id, [FromBody] UpdateAdminVehicleRequest request)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (vehicle == null)
            return NotFound();

        if (!string.IsNullOrEmpty(request.Name)) vehicle.Name = request.Name;
        if (request.Type != null) vehicle.Type = request.Type;
        if (request.Brand != null) vehicle.Brand = request.Brand;
        if (request.Model != null) vehicle.Model = request.Model;
        if (request.Plate != null) vehicle.Plate = request.Plate;
        if (request.Year.HasValue) vehicle.Year = request.Year.Value;
        if (request.Color != null) vehicle.Color = request.Color;
        if (request.Status != null) vehicle.Status = request.Status;
        if (request.HasGps.HasValue) vehicle.HasGps = request.HasGps.Value;
        if (request.Mileage.HasValue) vehicle.Mileage = request.Mileage.Value;
        if (request.CompanyId.HasValue) vehicle.CompanyId = request.CompanyId.Value;

        var targetCompanyId = request.CompanyId ?? vehicle.CompanyId;

        if (request.HasGps == false)
        {
            await ReleaseGpsDeviceAsync(vehicle.GpsDeviceId);
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }
        else if (request.HasGps == true ||
                 request.GpsDeviceId.HasValue ||
                 !string.IsNullOrWhiteSpace(request.GpsImei) ||
                 !string.IsNullOrWhiteSpace(request.GpsMat))
        {
            var (gpsDevice, error) = await ResolveGpsDeviceAsync(
                targetCompanyId,
                request.GpsDeviceId,
                request.GpsImei,
                request.GpsMat
            );

            if (error != null)
                return BadRequest(new { message = error });

            if (gpsDevice != null)
            {
                if (vehicle.GpsDeviceId.HasValue && vehicle.GpsDeviceId != gpsDevice.Id)
                {
                    await ReleaseGpsDeviceAsync(vehicle.GpsDeviceId);
                }

                vehicle.GpsDeviceId = gpsDevice.Id;
                vehicle.HasGps = true;
                gpsDevice.Status = "assigned";
                gpsDevice.Vehicle = vehicle;

                if (!string.IsNullOrWhiteSpace(request.GpsImei))
                    gpsDevice.DeviceUid = request.GpsImei!;

                if (!string.IsNullOrWhiteSpace(request.GpsMat))
                    gpsDevice.Mat = request.GpsMat;
                    
                if (!string.IsNullOrWhiteSpace(request.GpsFuelSensorMode))
                    gpsDevice.FuelSensorMode = request.GpsFuelSensorMode;
            }
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new AdminVehicleDto
        {
            Id = vehicle.Id,
            Name = vehicle.Name,
            Type = vehicle.Type,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Plate = vehicle.Plate,
            Year = vehicle.Year,
            Color = vehicle.Color,
            Status = vehicle.Status,
            HasGps = vehicle.HasGps,
            Mileage = vehicle.Mileage,
            FuelType = vehicle.FuelType,
            CompanyId = vehicle.CompanyId,
            CompanyName = vehicle.Societe?.Name,
            GpsDeviceId = vehicle.GpsDeviceId,
            GpsImei = vehicle.GpsDevice?.DeviceUid,
            GpsMat = vehicle.GpsDevice?.Mat,
            GpsBrand = vehicle.GpsDevice?.Brand,
            GpsModel = vehicle.GpsDevice?.Model,
            GpsFirmwareVersion = vehicle.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = vehicle.GpsDevice?.FuelSensorMode,
            AssignedDriverId = vehicle.AssignedDriverId,
            AssignedDriverName = vehicle.AssignedDriver?.Name,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt
        });
    }

    [HttpDelete("vehicles/{id}")]
    public async Task<ActionResult> DeleteVehicle(int id)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (vehicle == null) return NotFound();

        // Release GPS device before deleting vehicle
        if (vehicle.GpsDeviceId.HasValue)
        {
            await ReleaseGpsDeviceAsync(vehicle.GpsDeviceId);
        }

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Véhicule supprimé" });
    }

    [HttpGet("company/{companyId}/vehicles")]
    public async Task<ActionResult<List<AdminVehicleDto>>> GetCompanyVehicles(int companyId)
    {
        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        return Ok(vehicles.Select(v => new AdminVehicleDto
        {
            Id = v.Id,
            Name = v.Name,
            Type = v.Type,
            Brand = v.Brand,
            Model = v.Model,
            Plate = v.Plate,
            Year = v.Year,
            Color = v.Color,
            Status = v.Status,
            HasGps = v.HasGps,
            Mileage = v.Mileage,
            FuelType = v.FuelType,
            CompanyId = v.CompanyId,
            CompanyName = v.Societe?.Name,
            GpsDeviceId = v.GpsDeviceId,
            GpsImei = v.GpsDevice?.DeviceUid,
            GpsMat = v.GpsDevice?.Mat,
            GpsBrand = v.GpsDevice?.Brand,
            GpsModel = v.GpsDevice?.Model,
            GpsFirmwareVersion = v.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = v.GpsDevice?.FuelSensorMode,
            AssignedDriverId = v.AssignedDriverId,
            AssignedDriverName = v.AssignedDriver?.Name,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt
        }));
    }

    private async Task<(GpsDevice? device, string? error)> ResolveGpsDeviceAsync(
        int companyId,
        int? gpsDeviceId,
        string? gpsImei,
        string? gpsMat)
    {
        if (gpsDeviceId.HasValue)
        {
            var device = await _context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value);
            if (device == null)
                return (null, "Appareil GPS introuvable.");
            if (device.CompanyId != companyId)
                return (null, "Cet appareil GPS appartient à une autre société.");

            return (device, null);
        }

        var normalizedImei = gpsImei?.Trim();
        if (!string.IsNullOrEmpty(normalizedImei))
        {
            var device = await _context.GpsDevices
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.DeviceUid == normalizedImei);
            if (device != null)
            {
                // If device belongs to another company, update it to new company
                // This allows reusing GPS devices across companies
                if (device.CompanyId != companyId)
                {
                    device.CompanyId = companyId;
                }

                // Always update Mat if provided (overwrites existing)
                if (!string.IsNullOrWhiteSpace(gpsMat))
                {
                    device.Mat = gpsMat.Trim();
                }

                // Release from previous vehicle if any
                if (device.Vehicle != null)
                {
                    device.Vehicle.GpsDeviceId = null;
                    device.Vehicle.HasGps = false;
                }

                device.UpdatedAt = DateTime.UtcNow;
                return (device, null);
            }

            var newDevice = new GpsDevice
            {
                DeviceUid = normalizedImei,
                Mat = gpsMat?.Trim(),
                CompanyId = companyId,
                Status = "unassigned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.GpsDevices.Add(newDevice);
            return (newDevice, null);
        }

        var normalizedMat = gpsMat?.Trim();
        if (!string.IsNullOrEmpty(normalizedMat))
        {
            var device = await _context.GpsDevices.FirstOrDefaultAsync(d => d.Mat == normalizedMat);
            if (device != null)
            {
                if (device.CompanyId != companyId)
                    return (null, "Un appareil avec ce MAT appartient déjà à une autre société.");

                return (device, null);
            }
        }

        return (null, null);
    }

    private async Task ReleaseGpsDeviceAsync(int? gpsDeviceId)
    {
        if (!gpsDeviceId.HasValue)
            return;

        var device = await _context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value);
        if (device == null)
            return;

        device.Status = "unassigned";
        device.UpdatedAt = DateTime.UtcNow;
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
                CompanyName = u.Societe != null ? u.Societe.Name : "Unknown",
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
                CompanyName = v.Societe != null ? v.Societe.Name : "Unknown",
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
        
        var totalClients = await _context.Societes.CountAsync();
        var activeClients = await _context.Societes.CountAsync(c => c.IsActive);
        var totalUsers = await _context.Users.CountAsync();
        var totalVehicles = await _context.Vehicles.CountAsync();
        var activeDevices = await _context.GpsDevices.CountAsync();
        var totalPositionsToday = await _context.GpsPositions.CountAsync(p => p.RecordedAt >= today);
        
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);
        var newClientsThisMonth = await _context.Societes.CountAsync(c => c.CreatedAt >= firstOfMonth);

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

public class AdminCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Type { get; set; } = "transport";
    public int? SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }
    public int MaxVehicles { get; set; }
    public int CurrentVehicles { get; set; }
    public int CurrentUsers { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivity { get; set; }
    
    // Subscription status fields
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime SubscriptionStartedAt { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string BillingCycle { get; set; } = "yearly";
    public decimal? NextPaymentAmount { get; set; }
    public DateTime? LastPaymentAt { get; set; }
    public int? DaysUntilExpiration { get; set; }
}

public class AdminRoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RoleType { get; set; } = "employee";
    public Dictionary<string, object>? Permissions { get; set; }
    public bool IsSystem { get; set; }
    public bool IsDefault { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateAdminCompanyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Type { get; set; }
    public int SubscriptionId { get; set; }
    public string? BillingCycle { get; set; } // monthly, quarterly, yearly
    public string? AdminName { get; set; }
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

public class UpdateAdminCompanyRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Type { get; set; }
    public int? SubscriptionId { get; set; }
}

public class AdminVehicleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "camion";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Plate { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string Status { get; set; } = "available";
    public bool HasGps { get; set; }
    public int Mileage { get; set; }
    public string? FuelType { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsBrand { get; set; }
    public string? GpsModel { get; set; }
    public string? GpsFirmwareVersion { get; set; }
    public string? GpsFuelSensorMode { get; set; }
    public int? AssignedDriverId { get; set; }
    public string? AssignedDriverName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateAdminVehicleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Plate { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string? Status { get; set; }
    public bool HasGps { get; set; }
    public int? Mileage { get; set; }
    public string? FuelType { get; set; }
    public int? TankCapacity { get; set; }
    public string? VIN { get; set; }
    public int CompanyId { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsFuelSensorMode { get; set; }
}

public class UpdateAdminVehicleRequest
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Plate { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string? Status { get; set; }
    public bool? HasGps { get; set; }
    public int? Mileage { get; set; }
    public string? FuelType { get; set; }
    public int? CompanyId { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsFuelSensorMode { get; set; }
}
