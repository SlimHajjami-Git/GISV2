using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using System.Text.Json;
using MediatR;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicleById;
using GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.DeleteAdminVehicle;
using GisAPI.Application.Features.Admin.Users;
using GisAPI.Application.Features.Admin.Companies;
using GisAPI.Application.Features.Admin.Companies.Queries.GetCompanies;
using GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyById;
using GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyRoles;
using GisAPI.Application.Features.Admin.Companies.Commands.CreateCompany;
using GisAPI.Application.Features.Admin.Companies.Commands.UpdateCompany;
using GisAPI.Application.Features.Admin.Companies.Commands.ChangeCompanyStatus;
using GisAPI.Application.Features.Admin.Companies.Commands.DeleteCompany;
using GisAPI.Application.Features.Admin.Subscriptions;
using GisAPI.Application.Features.Admin.Subscriptions.Queries.GetSubscriptions;
using GisAPI.Application.Features.Admin.Subscriptions.Commands.CreateSubscription;
using GisAPI.Application.Features.Admin.Subscriptions.Commands.UpdateSubscription;
using GisAPI.Application.Features.Admin.Subscriptions.Commands.DeleteSubscription;
using GisAPI.Application.Features.Admin.Dashboard;
using GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;

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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Application.Common.Interfaces.IGpsHubService _gpsHub;
    private static MaintenanceModeDto _maintenanceMode = new() { Enabled = false, Pages = new List<string>(), Message = "" };

    public AdminController(GisDbContext context, IConfiguration configuration, IMediator mediator, IPermissionService permissionService, IHttpClientFactory httpClientFactory, Application.Common.Interfaces.IGpsHubService gpsHub)
    {
        _context = context;
        _configuration = configuration;
        _mediator = mediator;
        _permissionService = permissionService;
        _httpClientFactory = httpClientFactory;
        _gpsHub = gpsHub;
    }

    // ==================== SUBSCRIPTIONS ====================

    [HttpGet("subscriptions")]
    public async Task<ActionResult<List<AdminSubscriptionDto>>> GetSubscriptions()
    {
        var result = await _mediator.Send(new GetSubscriptionsQuery());
        return Ok(result);
    }

    [HttpGet("subscriptions/{id}")]
    public async Task<ActionResult<AdminSubscriptionDto>> GetSubscription(int id)
    {
        var all = await _mediator.Send(new GetSubscriptionsQuery());
        var sub = all.FirstOrDefault(s => s.Id == id);
        return sub != null ? Ok(sub) : NotFound();
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<AdminSubscriptionDto>> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        var result = await _mediator.Send(new CreateSubscriptionCommand(
            request.Name, request.Type, request.Price, request.MaxVehicles,
            request.GpsTracking, request.GpsInstallation
        ));
        return CreatedAtAction(nameof(GetSubscription), new { id = result.Id }, result);
    }

    [HttpPut("subscriptions/{id}")]
    public async Task<ActionResult<AdminSubscriptionDto>> UpdateSubscription(int id, [FromBody] CreateSubscriptionRequest request)
    {
        var result = await _mediator.Send(new UpdateSubscriptionCommand(
            id, request.Name, request.Type, request.Price, request.MaxVehicles,
            request.GpsTracking, request.GpsInstallation
        ));
        return result != null ? Ok(result) : NotFound();
    }

    [HttpDelete("subscriptions/{id}")]
    public async Task<ActionResult> DeleteSubscription(int id)
    {
        var error = await _mediator.Send(new DeleteSubscriptionCommand(id));
        if (error == "not_found") return NotFound();
        if (error != null) return BadRequest(new { message = error });
        return Ok(new { message = "Abonnement supprimé" });
    }

    // ==================== COMPANY MANAGEMENT ====================

    [HttpGet("company")]
    public async Task<ActionResult<List<Application.Features.Admin.Companies.AdminCompanyDto>>> GetCompanies([FromQuery] string? search, [FromQuery] string? status)
    {
        var result = await _mediator.Send(new GetCompaniesQuery(search, status));
        return Ok(result);
    }

    [HttpGet("company/{id}")]
    public async Task<ActionResult<Application.Features.Admin.Companies.AdminCompanyDto>> GetCompany(int id)
    {
        var result = await _mediator.Send(new GetCompanyByIdQuery(id));
        return result != null ? Ok(result) : NotFound();
    }

    [HttpGet("company/{id}/roles")]
    public async Task<ActionResult<List<Application.Features.Admin.Companies.AdminRoleDto>>> GetCompanyRoles(int id)
    {
        var result = await _mediator.Send(new GetCompanyRolesQuery(id));
        return Ok(result);
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
    public async Task<ActionResult<Application.Features.Admin.Companies.AdminCompanyDto>> CreateCompany([FromBody] CreateAdminCompanyRequest request)
    {
        try
        {
            var result = await _mediator.Send(new CreateCompanyCommand(
                request.Name, request.Email, request.Phone, request.Type,
                request.SubscriptionId, request.BillingCycle,
                request.AdminName, request.AdminEmail, request.AdminPassword
            ));
            return CreatedAtAction(nameof(GetCompany), new { id = result.Id }, result);
        }
        catch (GisAPI.Domain.Exceptions.DomainException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("company/{id}")]
    public async Task<ActionResult<Application.Features.Admin.Companies.AdminCompanyDto>> UpdateCompany(int id, [FromBody] UpdateAdminCompanyRequest request)
    {
        var result = await _mediator.Send(new UpdateCompanyCommand(
            id, request.Name, request.Email, request.Phone, request.Type, request.SubscriptionId
        ));
        return result != null ? Ok(result) : NotFound();
    }

    [HttpPost("company/{id}/suspend")]
    public async Task<ActionResult> SuspendCompany(int id)
    {
        var found = await _mediator.Send(new ChangeCompanyStatusCommand(id, false));
        return found ? Ok(new { message = "Société suspendue" }) : NotFound();
    }

    [HttpPost("company/{id}/activate")]
    public async Task<ActionResult> ActivateCompany(int id)
    {
        var found = await _mediator.Send(new ChangeCompanyStatusCommand(id, true));
        return found ? Ok(new { message = "Société activée" }) : NotFound();
    }

    [HttpDelete("company/{id}")]
    public async Task<ActionResult> DeleteCompany(int id)
    {
        var found = await _mediator.Send(new DeleteCompanyCommand(id));
        return found ? Ok(new { message = "Société supprimée" }) : NotFound();
    }

    /// <summary>
    /// Modifie directement la DATE D'ÉCHÉANCE de l'abonnement d'une société
    /// (fiche société admin). Si la nouvelle date est dans le futur et que la
    /// société était marquée expirée, elle repasse active — les utilisateurs
    /// bloqués retrouvent l'accès immédiatement (push SignalR).
    /// </summary>
    [HttpPut("company/{id}/subscription-expiry")]
    public async Task<ActionResult> SetSubscriptionExpiry(int id, [FromBody] SetSubscriptionExpiryRequest request)
    {
        var societe = await _context.Societes.FindAsync(id);
        if (societe == null) return NotFound();

        // Fin de journée UTC : une échéance « au 15/08 » reste valable le 15/08.
        var expires = DateTime.SpecifyKind(request.ExpiresAt.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
        societe.SubscriptionExpiresAt = expires;
        if (expires > DateTime.UtcNow && societe.SubscriptionStatus == "expired")
            societe.SubscriptionStatus = "active";
        societe.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        try { await _gpsHub.SendSubscriptionChangedAsync(societe.Id, societe.SubscriptionStatus); } catch { /* best effort */ }
        return Ok(new { subscriptionExpiresAt = societe.SubscriptionExpiresAt, subscriptionStatus = societe.SubscriptionStatus });
    }

    /// <summary>
    /// « Marquer comme payé » : prolonge l'abonnement d'UN cycle de facturation
    /// (à partir de l'échéance courante si elle est future, sinon d'aujourd'hui),
    /// enregistre le paiement (LastPaymentAt=maintenant), repasse la société
    /// active et recalcule le montant du prochain cycle. Réutilise la logique
    /// de renouvellement standard (RenewSubscriptionCommand).
    /// </summary>
    [HttpPost("company/{id}/mark-paid")]
    public async Task<ActionResult> MarkSubscriptionPaid(int id)
    {
        var societe = await _context.Societes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (societe == null) return NotFound();

        var result = await _mediator.Send(new GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription.RenewSubscriptionCommand(
            id, string.IsNullOrWhiteSpace(societe.BillingCycle) ? "yearly" : societe.BillingCycle));

        try { await _gpsHub.SendSubscriptionChangedAsync(id, "active"); } catch { /* best effort */ }
        return Ok(result);
    }

    /// <summary>
    /// Interrupteur PAR SOCIÉTÉ de la suspension automatique à l'expiration.
    /// enabled=false : la société expirée n'est jamais bloquée automatiquement
    /// (bannière rouge permanente, marquée impayée) — seule la suspension
    /// manuelle coupe l'accès. La bascule prend effet immédiatement (le
    /// middleware ré-évalue à chaque requête).
    /// </summary>
    [HttpPut("company/{id}/auto-suspend")]
    public async Task<ActionResult> SetAutoSuspend(int id, [FromBody] SetAutoSuspendRequest request)
    {
        var societe = await _context.Societes.FindAsync(id);
        if (societe == null) return NotFound();

        societe.AutoSuspendEnabled = request.Enabled;
        societe.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { autoSuspendEnabled = societe.AutoSuspendEnabled });
    }

    /// <summary>
    /// Vue « facturation » plateforme : toutes les sociétés à surveiller —
    /// expirent sous 30 j, expirées en grâce (= facture IMPAYÉE), bloquées,
    /// ou suspendues manuellement. Alimente le bandeau d'alerte et la cloche
    /// de l'admin. Les règles viennent de SubscriptionPolicy (mêmes seuils que
    /// la bannière client et le digest quotidien).
    /// </summary>
    [HttpGet("billing/overview")]
    public async Task<ActionResult> GetBillingOverview()
    {
        var now = DateTime.UtcNow;
        var societes = await _context.Societes.AsNoTracking().ToListAsync();

        var items = societes
            .Select(s => new { Societe = s, State = Application.Common.SubscriptionPolicy.Evaluate(s, now) })
            .Where(x => x.State.Level != "none")
            .OrderBy(x => x.State.Level == "blocked" ? 0 : x.State.Reason == "grace" ? 1 : 2)
            .ThenBy(x => x.State.DaysRemaining ?? int.MaxValue)
            .Select(x => new
            {
                id = x.Societe.Id,
                name = x.Societe.Name,
                level = x.State.Level,                    // warning | danger | blocked
                reason = x.State.Reason,                  // expiring | grace | expired | suspended | cancelled
                expiresAt = x.Societe.SubscriptionExpiresAt,
                daysRemaining = x.State.DaysRemaining,
                graceDaysLeft = x.State.GraceDaysLeft,
                unpaid = x.State.Reason is "grace" or "expired",
                amountDue = x.Societe.NextPaymentAmount,
                lastPaymentAt = x.Societe.LastPaymentAt,
                subscriptionStatus = x.Societe.SubscriptionStatus,
                isActive = x.Societe.IsActive,
                autoSuspendEnabled = x.Societe.AutoSuspendEnabled
            })
            .ToList();

        return Ok(new { count = items.Count, items });
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
        var result = await _mediator.Send(new GetAdminVehicleByIdQuery(id));
        return result != null ? Ok(result) : NotFound();
    }

    [HttpPost("vehicles")]
    public async Task<ActionResult<AdminVehicleDto>> CreateVehicle([FromBody] CreateAdminVehicleRequest request)
    {
        Console.WriteLine($"[AdminController.CreateVehicle] HasGps={request.HasGps}, GpsDeviceId={request.GpsDeviceId}, GpsImei={request.GpsImei}, GpsSimNumber='{request.GpsSimNumber}', GpsSimOperator='{request.GpsSimOperator}'");
        var result = await _mediator.Send(new CreateAdminVehicleCommand(
            request.Name, request.Type, request.Brand, request.Model, request.Plate,
            request.Year, request.Color, request.Status, request.HasGps, request.Mileage,
            request.FuelType, request.FuelTankCapacity, request.CompanyId,
            request.GpsDeviceId, request.GpsImei, request.GpsMat,
            request.GpsBrand, request.GpsModel, request.GpsFirmwareVersion, request.GpsFuelSensorMode,
            request.GpsSimNumber, request.GpsSimOperator, request.GpsInstallationDate
        ));
        if (!result.Success) return BadRequest(new { message = result.Error });
        return CreatedAtAction(nameof(GetVehicle), new { id = result.Vehicle!.Id }, result.Vehicle);
    }

    [HttpPut("vehicles/{id}")]
    public async Task<ActionResult<AdminVehicleDto>> UpdateVehicle(int id, [FromBody] UpdateAdminVehicleRequest request)
    {
        Console.WriteLine($"[AdminController.UpdateVehicle] Id={id}, HasGps={request.HasGps}, GpsDeviceId={request.GpsDeviceId}, GpsSimNumber='{request.GpsSimNumber}', GpsSimOperator='{request.GpsSimOperator}'");
        var result = await _mediator.Send(new UpdateAdminVehicleCommand(
            id, request.Name, request.Type, request.Brand, request.Model, request.Plate,
            request.Year, request.Color, request.Status, request.HasGps, request.Mileage,
            request.FuelType, request.FuelTankCapacity, request.CompanyId,
            request.GpsDeviceId, request.GpsImei, request.GpsMat,
            request.GpsBrand, request.GpsModel, request.GpsFirmwareVersion, request.GpsFuelSensorMode,
            request.GpsSimNumber, request.GpsSimOperator, request.GpsInstallationDate
        ));
        if (!result.Success && result.Error == "not_found") return NotFound();
        if (!result.Success) return BadRequest(new { message = result.Error });
        return Ok(result.Vehicle);
    }

    [HttpDelete("vehicles/{id}")]
    public async Task<ActionResult> DeleteVehicle(int id)
    {
        var found = await _mediator.Send(new DeleteAdminVehicleCommand(id));
        return found ? Ok(new { message = "Véhicule supprimé" }) : NotFound();
    }

    [HttpGet("company/{companyId}/vehicles")]
    public async Task<ActionResult<List<AdminVehicleDto>>> GetCompanyVehicles(int companyId)
    {
        var vehicles = await _mediator.Send(new GetAdminVehiclesQuery(null, companyId, null));
        return Ok(vehicles);
    }

    /// <summary>
    /// Calypso 9 p6 — one-shot backfill: re-push every vehicle's already-stored
    /// km/h speed limit to its boitier (AJ+CONFN). Platform-wide (all companies).
    /// Limits set before the device-programming wiring existed only touched the
    /// DB; this brings the hardware in line. Safe to re-run.
    /// </summary>
    [HttpPost("speed-limits/sync-devices")]
    public async Task<ActionResult<SyncSpeedLimitsResult>> SyncAllSpeedLimitsToDevices()
    {
        var result = await _mediator.Send(new SyncAllSpeedLimitsToDevicesCommand());
        return Ok(result);
    }

    // ==================== ADMIN VEHICLES WITH POSITIONS (ALL COMPANIES) ====================

    [HttpGet("vehicles/with-positions")]
    public async Task<ActionResult> GetAllVehiclesWithPositions([FromQuery] int? companyId)
    {
        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .Include(v => v.Societe)
            .AsQueryable();

        if (companyId.HasValue)
            vehicleQuery = vehicleQuery.Where(v => v.CompanyId == companyId.Value);

        var vehicles = await vehicleQuery.ToListAsync();

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Fetch latest position per device (batch query)
        var latestPositions = new Dictionary<int, dynamic>();
        foreach (var deviceId in deviceIds)
        {
            var pos = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == deviceId)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => new {
                    p.Id, p.Latitude, p.Longitude, p.SpeedKph, p.CourseDeg,
                    p.IgnitionOn, p.RecordedAt, p.FuelRaw, p.TemperatureC,
                    p.PowerVoltage, p.Address, p.OdometerKm
                })
                .FirstOrDefaultAsync();
            if (pos != null) latestPositions[deviceId] = pos;
        }

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 30;

            var ignitionOn = (bool?)(position?.IgnitionOn) ?? false;
            var rawSpeed = (double?)(position?.SpeedKph) ?? 0.0;

            if (ignitionOn && rawSpeed <= 1 && position != null)
            {
                var positionAge = (DateTime.UtcNow - (DateTime)position.RecordedAt).TotalMinutes;
                if (positionAge > 10) ignitionOn = false;
            }

            var currentSpeed = ignitionOn ? Math.Round(rawSpeed) : 0.0;

            // Fuel conversion
            int? fuelLevel = null;
            var fuelRaw = (int?)position?.FuelRaw;
            if (fuelRaw.HasValue)
            {
                var fuelMode = v.GpsDevice?.FuelSensorMode ?? "raw_255";
                var tankCapacity = v.FuelTankCapacity ?? 60;
                fuelLevel = fuelMode switch
                {
                    "percent" => fuelRaw.Value,
                    "raw_255" => (int)Math.Round(fuelRaw.Value / 255.0 * 100.0),
                    "liters" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 100.0 / tankCapacity) : fuelRaw.Value,
                    "half_liter" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(fuelRaw.Value * 0.5),
                    _ => fuelRaw.Value
                };
                if (fuelLevel > 100) fuelLevel = 100;
                if (fuelLevel < 0) fuelLevel = 0;
            }

            // Temperature filter
            var temperature = (short?)position?.TemperatureC;
            if (temperature.HasValue && (temperature.Value < -100 || temperature.Value > 200))
                temperature = null;

            return new {
                id = v.Id,
                name = v.Name,
                type = v.Type,
                brand = v.Brand,
                model = v.Model,
                plate = v.Plate,
                status = v.Status,
                hasGps = v.HasGps,
                deviceUid = v.GpsDevice?.DeviceUid,
                lastCommunication = lastComm,
                isOnline,
                companyId = v.CompanyId,
                companyName = v.Societe?.Name,
                mileage = v.Mileage,
                lastPosition = position != null ? new {
                    id = (long)position.Id,
                    latitude = (double)position.Latitude,
                    longitude = (double)position.Longitude,
                    speedKph = currentSpeed,
                    courseDeg = (double?)(position.CourseDeg) ?? 0.0,
                    ignitionOn,
                    recordedAt = (DateTime)position.RecordedAt,
                    fuelRaw = (int?)position.FuelRaw,
                    temperatureC = temperature,
                    address = (string?)position.Address,
                    odometerKm = (long?)position.OdometerKm
                } : (object?)null,
                stats = new {
                    currentSpeed,
                    fuelLevel,
                    temperature,
                    isMoving = ignitionOn && rawSpeed > 5
                }
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("vehicles/{vehicleId}/history")]
    public async Task<ActionResult> GetAdminVehicleHistory(
        int vehicleId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int maxPoints = 5000)
    {
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId);

        if (vehicle == null) return NotFound();
        if (!vehicle.GpsDeviceId.HasValue) return Ok(new List<object>());

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;
        var fromUtc = from.Value.Kind == DateTimeKind.Utc ? from.Value : from.Value.ToUniversalTime();
        var toUtc = to.Value.Kind == DateTimeKind.Utc ? to.Value : to.Value.ToUniversalTime();
        maxPoints = Math.Clamp(maxPoints, 100, 50000);

        var query = _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value && p.RecordedAt >= fromUtc && p.RecordedAt <= toUtc)
            .OrderBy(p => p.RecordedAt);

        var totalCount = await query.CountAsync();

        List<object> positions;
        if (totalCount <= maxPoints)
        {
            positions = await query.Select(p => (object)new {
                p.Id, p.Latitude, p.Longitude, speedKph = p.SpeedKph, courseDeg = p.CourseDeg,
                ignitionOn = p.IgnitionOn, recordedAt = p.RecordedAt, fuelRaw = p.FuelRaw,
                rpm = p.Rpm, odometerKm = p.OdometerKm, temperatureC = p.TemperatureC,
                isRealTime = p.IsRealTime, address = p.Address, createdAt = p.CreatedAt
            }).ToListAsync();
        }
        else
        {
            // Downsample: take every Nth record
            var step = (double)totalCount / maxPoints;
            var allIds = await query.Select(p => p.Id).ToListAsync();
            var sampledIds = new HashSet<long>();
            for (double i = 0; i < allIds.Count; i += step)
                sampledIds.Add(allIds[(int)i]);
            // Always include first and last
            sampledIds.Add(allIds[0]);
            sampledIds.Add(allIds[^1]);

            positions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => sampledIds.Contains(p.Id))
                .OrderBy(p => p.RecordedAt)
                .Select(p => (object)new {
                    p.Id, p.Latitude, p.Longitude, speedKph = p.SpeedKph, courseDeg = p.CourseDeg,
                    ignitionOn = p.IgnitionOn, recordedAt = p.RecordedAt, fuelRaw = p.FuelRaw,
                    rpm = p.Rpm, odometerKm = p.OdometerKm, temperatureC = p.TemperatureC,
                    isRealTime = p.IsRealTime, address = p.Address, createdAt = p.CreatedAt
                }).ToListAsync();
        }

        return Ok(positions);
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

        // Check GPS Ingest Service (actual HTTP ping + data stats)
        var recentPositions = await _context.GpsPositions
            .Where(p => p.RecordedAt > DateTime.UtcNow.AddMinutes(-5))
            .CountAsync();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var gpsResponse = await client.GetAsync("http://gps-ingest:3000/api/health");
            sw2.Stop();

            services.Add(new ServiceHealthDto
            {
                Name = "GPS Ingest Service",
                Status = gpsResponse.IsSuccessStatusCode ? "healthy" : "degraded",
                ResponseTime = (int)sw2.ElapsedMilliseconds,
                LastCheck = DateTime.UtcNow,
                Uptime = gpsResponse.IsSuccessStatusCode ? 99.9 : 50,
                Details = new Dictionary<string, object>
                {
                    { "recentPositions", recentPositions },
                    { "status", gpsResponse.IsSuccessStatusCode ? (recentPositions > 0 ? "receiving data" : "online, no recent data") : "unreachable" }
                }
            });
        }
        catch
        {
            services.Add(new ServiceHealthDto
            {
                Name = "GPS Ingest Service",
                Status = "down",
                ResponseTime = 0,
                LastCheck = DateTime.UtcNow,
                Uptime = 0,
                Details = new Dictionary<string, object>
                {
                    { "recentPositions", recentPositions },
                    { "status", "unreachable" }
                }
            });
        }

        // Check RabbitMQ (actual HTTP ping to management API)
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var rabbitUser = _configuration["RabbitMQ:Username"] ?? "guest";
            var rabbitPass = _configuration["RabbitMQ:Password"] ?? "guest";
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{rabbitUser}:{rabbitPass}")));
            var sw3 = System.Diagnostics.Stopwatch.StartNew();
            var rabbitResponse = await client.GetAsync("http://rabbitmq:15672/api/overview");
            sw3.Stop();

            services.Add(new ServiceHealthDto
            {
                Name = "RabbitMQ",
                Status = rabbitResponse.IsSuccessStatusCode ? "healthy" : "degraded",
                ResponseTime = (int)sw3.ElapsedMilliseconds,
                LastCheck = DateTime.UtcNow,
                Uptime = rabbitResponse.IsSuccessStatusCode ? 99.95 : 50,
                Details = new Dictionary<string, object>
                {
                    { "status", rabbitResponse.IsSuccessStatusCode ? "connected" : "management API unreachable" }
                }
            });
        }
        catch
        {
            services.Add(new ServiceHealthDto
            {
                Name = "RabbitMQ",
                Status = "down",
                ResponseTime = 0,
                LastCheck = DateTime.UtcNow,
                Uptime = 0,
                Details = new Dictionary<string, object>
                {
                    { "status", "unreachable" }
                }
            });
        }

        // Check Frontend (actual HTTP ping)
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var sw4 = System.Diagnostics.Stopwatch.StartNew();
            var frontendResponse = await client.GetAsync("http://frontend:80");
            sw4.Stop();

            services.Add(new ServiceHealthDto
            {
                Name = "Frontend",
                Status = frontendResponse.IsSuccessStatusCode ? "healthy" : "degraded",
                ResponseTime = (int)sw4.ElapsedMilliseconds,
                LastCheck = DateTime.UtcNow,
                Uptime = frontendResponse.IsSuccessStatusCode ? 99.99 : 50,
                Details = new Dictionary<string, object>
                {
                    { "version", "2.0.0" }
                }
            });
        }
        catch
        {
            services.Add(new ServiceHealthDto
            {
                Name = "Frontend",
                Status = "down",
                ResponseTime = 0,
                LastCheck = DateTime.UtcNow,
                Uptime = 0,
                Details = new Dictionary<string, object>
                {
                    { "version", "2.0.0" }
                }
            });
        }

        return Ok(services);
    }

    // ==================== ACTIVITY LOGS ====================

    [HttpGet("activity-logs")]
    public async Task<ActionResult<List<Application.Features.Admin.Dashboard.ActivityLogDto>>> GetActivityLogs([FromQuery] int limit = 50)
    {
        var logs = await _mediator.Send(new GetActivityLogsQuery(limit));
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
    public async Task<ActionResult<Application.Features.Admin.Dashboard.DashboardStatsDto>> GetDashboardStats()
    {
        var stats = await _mediator.Send(new GetDashboardStatsQuery());
        return Ok(stats);
    }

    [HttpGet("dashboard/feature-usage")]
    public ActionResult<List<FeatureUsageDto>> GetFeatureUsage()
    {
        // Feature usage tracking would require analytics implementation
        // Return empty for now - can be implemented with proper tracking
        return Ok(new List<FeatureUsageDto>());
    }

    // ==================== DEVIS ====================

    private static readonly string[] EstimateStatuses = { "draft", "sent", "accepted", "rejected" };

    private static object MapEstimate(Estimate e)
    {
        var subtotal = e.Items.Sum(i => i.Quantity * i.UnitPrice);
        var discountAmount = Math.Round(subtotal * e.DiscountPercent / 100m, 3);
        var afterDiscount = subtotal - discountAmount;
        var taxAmount = Math.Round(afterDiscount * e.TaxPercent / 100m, 3);
        return new
        {
            e.Id, e.Number, e.CompanyId,
            companyName = e.Company?.Name,
            e.ClientName, e.ClientEmail, e.ClientPhone, e.ClientAddress,
            e.Status, e.IssueDate, e.ValidUntil,
            e.DiscountPercent, e.TaxPercent, e.Notes,
            e.CreatedAt, e.UpdatedAt,
            items = e.Items.OrderBy(i => i.SortOrder).Select(i => new
            {
                i.Id, i.Description, i.Quantity, i.UnitPrice,
                total = i.Quantity * i.UnitPrice
            }).ToList(),
            subtotal, discountAmount, taxAmount,
            total = afterDiscount + taxAmount
        };
    }

    [HttpGet("estimates")]
    public async Task<ActionResult> GetEstimates()
    {
        var list = await _context.Estimates.AsNoTracking()
            .Include(e => e.Items).Include(e => e.Company)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
        return Ok(list.Select(MapEstimate).ToList());
    }

    [HttpGet("estimates/{id:int}")]
    public async Task<ActionResult> GetEstimate(int id)
    {
        var e = await _context.Estimates.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id);
        return e == null ? NotFound() : Ok(MapEstimate(e));
    }

    public record EstimateItemInput(string Description, decimal Quantity, decimal UnitPrice);
    public record EstimateInput(int? CompanyId, string ClientName, string? ClientEmail, string? ClientPhone,
        string? ClientAddress, DateTime? ValidUntil, decimal DiscountPercent, decimal TaxPercent,
        string? Notes, List<EstimateItemInput> Items);

    private static string? ValidateEstimate(EstimateInput r)
    {
        if (string.IsNullOrWhiteSpace(r.ClientName)) return "Le nom du client est requis.";
        if (r.Items == null || r.Items.Count == 0 || r.Items.All(i => string.IsNullOrWhiteSpace(i.Description)))
            return "Ajoutez au moins une ligne au devis.";
        if (r.DiscountPercent is < 0 or > 100) return "Remise invalide (0-100 %).";
        if (r.TaxPercent is < 0 or > 100) return "TVA invalide (0-100 %).";
        return null;
    }

    private static void ApplyEstimateInput(Estimate e, EstimateInput r)
    {
        e.CompanyId = r.CompanyId;
        e.ClientName = r.ClientName.Trim();
        e.ClientEmail = r.ClientEmail?.Trim();
        e.ClientPhone = r.ClientPhone?.Trim();
        e.ClientAddress = r.ClientAddress?.Trim();
        e.ValidUntil = r.ValidUntil;
        e.DiscountPercent = r.DiscountPercent;
        e.TaxPercent = r.TaxPercent;
        e.Notes = r.Notes;
        e.UpdatedAt = DateTime.UtcNow;
        e.Items.Clear();
        int order = 0;
        foreach (var i in r.Items.Where(i => !string.IsNullOrWhiteSpace(i.Description)))
            e.Items.Add(new EstimateItem
            {
                Description = i.Description.Trim(),
                Quantity = i.Quantity <= 0 ? 1 : i.Quantity,
                UnitPrice = i.UnitPrice < 0 ? 0 : i.UnitPrice,
                SortOrder = order++
            });
    }

    [HttpPost("estimates")]
    public async Task<ActionResult> CreateEstimate([FromBody] EstimateInput request)
    {
        var error = ValidateEstimate(request);
        if (error != null) return BadRequest(new { message = error });

        // Numérotation DEV-<année>-<seq> : max de l'année + 1 (volume admin faible,
        // pas de concurrence significative ; l'index unique protège en dernier recours).
        var year = DateTime.UtcNow.Year;
        var prefix = $"DEV-{year}-";
        var lastSeq = await _context.Estimates
            .Where(e => e.Number.StartsWith(prefix))
            .Select(e => e.Number.Substring(prefix.Length))
            .ToListAsync();
        var next = lastSeq.Select(s => int.TryParse(s, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

        var estimate = new Estimate
        {
            Number = $"{prefix}{next:D4}",
            Status = "draft",
            IssueDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        ApplyEstimateInput(estimate, request);
        _context.Estimates.Add(estimate);
        await _context.SaveChangesAsync();

        var created = await _context.Estimates.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Company).FirstAsync(x => x.Id == estimate.Id);
        return Ok(MapEstimate(created));
    }

    [HttpPut("estimates/{id:int}")]
    public async Task<ActionResult> UpdateEstimate(int id, [FromBody] EstimateInput request)
    {
        var error = ValidateEstimate(request);
        if (error != null) return BadRequest(new { message = error });

        var e = await _context.Estimates.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();

        ApplyEstimateInput(e, request);
        await _context.SaveChangesAsync();

        var updated = await _context.Estimates.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Company).FirstAsync(x => x.Id == id);
        return Ok(MapEstimate(updated));
    }

    [HttpPut("estimates/{id:int}/status")]
    public async Task<ActionResult> UpdateEstimateStatus(int id, [FromBody] UpdateEstimateStatusRequest request)
    {
        if (!EstimateStatuses.Contains(request.Status))
            return BadRequest(new { message = "Statut invalide." });
        var e = await _context.Estimates.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.Status = request.Status;
        e.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(new { e.Id, e.Status });
    }

    [HttpDelete("estimates/{id:int}")]
    public async Task<ActionResult> DeleteEstimate(int id)
    {
        var e = await _context.Estimates.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        _context.Estimates.Remove(e);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Devis supprimé." });
    }

    // ==================== TEST NOTIFICATIONS ====================

    [HttpGet("notifications/users")]
    public async Task<ActionResult> GetUsersForNotification()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.Status == "active")
            .OrderBy(u => u.CompanyId)
            .ThenBy(u => u.FirstName)
            .Select(u => new
            {
                u.Id,
                FullName = u.FirstName + " " + u.LastName,
                u.Email,
                u.CompanyId,
                CompanyName = u.Societe != null ? u.Societe.Name : ""
            })
            .Take(500)
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("notifications/send")]
    public async Task<ActionResult> SendTestNotification(
        [FromBody] SendTestNotificationRequest request,
        [FromServices] INotificationService notificationService)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId);
        if (user == null) return NotFound(new { message = "Utilisateur introuvable" });

        var notification = await notificationService.CreateAndSendAsync(
            companyId: user.CompanyId,
            userId: user.Id,
            type: request.Type,
            title: request.Title,
            message: request.Message,
            priority: request.Priority,
            referenceType: "test",
            referenceId: null,
            actionUrl: "/dashboard",
            metadata: new Dictionary<string, object>
            {
                { "source", "admin_test" },
                { "sentAt", DateTime.UtcNow.ToString("O") }
            }
        );

        return Ok(new
        {
            success = true,
            notificationId = notification.Id,
            sentTo = user.FirstName + " " + user.LastName,
            sentToEmail = user.Email
        });
    }

    // ==================== DEAD LETTER QUEUE ====================

    [HttpGet("dlq/messages")]
    public async Task<ActionResult> GetDeadLetterMessages([FromQuery] int limit = 50)
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:Username"] ?? _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            var dlqName = _configuration["RabbitMQ:DeadLetterQueue"] ?? "gis.dead-letters";
            var queueInfo = await channel.QueueDeclarePassiveAsync(dlqName);

            var messages = new List<object>();
            for (int i = 0; i < limit; i++)
            {
                var result = await channel.BasicGetAsync(dlqName, autoAck: false);
                if (result == null) break;

                var body = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
                var deathInfo = result.BasicProperties.Headers != null &&
                    result.BasicProperties.Headers.TryGetValue("x-death", out var xDeath)
                    ? xDeath : null;

                messages.Add(new
                {
                    deliveryTag = result.DeliveryTag,
                    body,
                    exchange = result.Exchange,
                    routingKey = result.RoutingKey,
                    redelivered = result.Redelivered,
                    xDeath = deathInfo?.ToString()
                });

                // Reject back without requeue so it stays in DLQ
                await channel.BasicNackAsync(result.DeliveryTag, false, true);
            }

            return Ok(new
            {
                queueName = dlqName,
                messageCount = queueInfo.MessageCount,
                messages
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("dlq/purge")]
    public async Task<ActionResult> PurgeDeadLetterQueue()
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:Username"] ?? _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            var dlqName = _configuration["RabbitMQ:DeadLetterQueue"] ?? "gis.dead-letters";
            var purged = await channel.QueuePurgeAsync(dlqName);

            return Ok(new { queueName = dlqName, purgedMessages = purged });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("dlq/stats")]
    public async Task<ActionResult> GetDeadLetterStats()
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory
            {
                HostName = _configuration["RabbitMQ:Host"] ?? _configuration["RabbitMQ:HostName"] ?? "localhost",
                Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
                UserName = _configuration["RabbitMQ:Username"] ?? _configuration["RabbitMQ:UserName"] ?? "guest",
                Password = _configuration["RabbitMQ:Password"] ?? "guest"
            };

            using var connection = await factory.CreateConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            var dlqName = _configuration["RabbitMQ:DeadLetterQueue"] ?? "gis.dead-letters";
            var queueInfo = await channel.QueueDeclarePassiveAsync(dlqName);

            return Ok(new
            {
                queueName = dlqName,
                messageCount = queueInfo.MessageCount,
                consumerCount = queueInfo.ConsumerCount
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ==================== AUTO-RECOVERY LOG ====================

    [HttpGet("auto-recovery")]
    public async Task<IActionResult> GetAutoRecoveryLog([FromQuery] int limit = 100, [FromQuery] int? companyId = null)
    {
        var query = _context.DeviceCommands
            .AsNoTracking()
            .Where(c => c.Source == "auto_recovery")
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(c => c.CompanyId == companyId.Value);

        var commands = await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new {
                c.Id,
                c.DeviceId,
                deviceMat = c.Device != null ? c.Device.Mat : null,
                deviceUid = c.Device != null ? c.Device.DeviceUid : null,
                vehicleName = c.Vehicle != null ? c.Vehicle.Name : null,
                vehiclePlate = c.Vehicle != null ? c.Vehicle.Plate : null,
                driverPhone = c.Device != null ? c.Device.SimNumber : null,
                companyName = c.Device != null && c.Device.Societe != null ? c.Device.Societe.Name : null,
                c.CompanyId,
                c.CommandType,
                c.CommandText,
                flagsHex = c.ErrorMessage,
                c.Status,
                c.SentAt,
                c.CreatedAt
            })
            .ToListAsync();

        return Ok(commands);
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

public record SetAutoSuspendRequest(bool Enabled);

public record SetSubscriptionExpiryRequest(DateTime ExpiresAt);

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
    public int? FuelTankCapacity { get; set; }
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
    public int? FuelTankCapacity { get; set; }
    public string? VIN { get; set; }
    public int CompanyId { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsBrand { get; set; }
    public string? GpsModel { get; set; }
    public string? GpsFirmwareVersion { get; set; }
    public string? GpsFuelSensorMode { get; set; }
    public string? GpsSimNumber { get; set; }
    public string? GpsSimOperator { get; set; }
    public DateTime? GpsInstallationDate { get; set; }
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
    public int? FuelTankCapacity { get; set; }
    public int? CompanyId { get; set; }
    public int? GpsDeviceId { get; set; }
    public string? GpsImei { get; set; }
    public string? GpsMat { get; set; }
    public string? GpsBrand { get; set; }
    public string? GpsModel { get; set; }
    public string? GpsFirmwareVersion { get; set; }
    public string? GpsFuelSensorMode { get; set; }
    public string? GpsSimNumber { get; set; }
    public string? GpsSimOperator { get; set; }
    public DateTime? GpsInstallationDate { get; set; }
}

public class SendTestNotificationRequest
{
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "info";
    public string Priority { get; set; } = "normal";
}
