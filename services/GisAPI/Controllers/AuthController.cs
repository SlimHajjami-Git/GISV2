using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Application.Features.Auth.Commands.RefreshToken;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly GisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public AuthController(IMediator mediator, GisDbContext context, IPasswordHasher passwordHasher)
    {
        _mediator = mediator;
        _context = context;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password));
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request)
    {
        try
        {
            var result = await _mediator.Send(new RefreshTokenCommand(request.Token, request.RefreshToken));
            return Ok(result);
        }
        catch (GisAPI.Domain.Exceptions.DomainException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("seed")]
    public async Task<ActionResult> SeedDatabase()
    {
        // Only allow seeding in Development — NEVER in Production
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment != "Development")
        {
            return NotFound();
        }

        try
        {
            // Check if data already exists
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == "admin@belive.ma");
            if (existingUser != null)
            {
                existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Calypso@2026+");
                existingUser.Status = "active";
                await _context.SaveChangesAsync();
                return Ok(new { message = "User admin@belive.ma updated" });
            }

            // Create subscription type
            var subscriptionType = await _context.SubscriptionTypes.FirstOrDefaultAsync();
            if (subscriptionType == null)
            {
                subscriptionType = new SubscriptionType
                {
                    Name = "Plan Pro",
                    Code = "plan-pro",
                    TargetCompanyType = "all",
                    YearlyPrice = 999.00m,
                    GpsTracking = true,
                    GpsInstallation = true,
                    MaxVehicles = 100,
                    MaxUsers = 20,
                    MaxGpsDevices = 100,
                    MaxGeofences = 50,
                    IsActive = true,
                    ModuleEmployees = true,
                    ModuleDashboard = true,
                    ModuleVehicles = true,
                    ModuleMaintenance = true,
                    ModuleCosts = true,
                    ModuleReports = true,
                    ModuleSettings = true,
                    ModuleUsers = true,
                    ModuleDocuments = true,
                    ModuleMonitoring = true,
                    ModuleGeofences = true,
                    ModuleSuppliers = true,
                    ModuleAccidents = true
                };
                _context.SubscriptionTypes.Add(subscriptionType);
                await _context.SaveChangesAsync();
            }

            // Create company
            var company = await _context.Societes.FirstOrDefaultAsync(c => c.Name == "Belive");
            if (company == null)
            {
                company = new Societe
                {
                    Name = "Belive",
                    Type = "transport",
                    Address = "Sfax, Tunisie",
                    City = "Sfax",
                    Country = "TN",
                    Phone = "+216 74 000 000",
                    Email = "contact@belive.tn",
                    SubscriptionTypeId = subscriptionType.Id,
                    IsActive = true,
                    SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
                };
                _context.Societes.Add(company);
                await _context.SaveChangesAsync();
            }

            // Get or create admin role for the company
            var adminRole = await _context.Roles.FirstOrDefaultAsync(r => r.SocieteId == company.Id && r.IsCompanyAdmin);
            if (adminRole == null)
            {
                adminRole = new Role
                {
                    Name = "Administrateur",
                    SocieteId = company.Id,
                    IsCompanyAdmin = true,
                    Permissions = new Dictionary<string, object> { { "all", true } }
                };
                _context.Roles.Add(adminRole);
                await _context.SaveChangesAsync();
            }

            // Create admin user
            var adminUser = new User
            {
                Name = "Admin Belive",
                Email = "admin@belive.ma",
                Phone = "+216 00 000 000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Calypso@2026+"),
                RoleId = adminRole.Id,
                Status = "active",
                CompanyId = company.Id
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Database seeded successfully",
                email = "admin@belive.ma",
                companyId = company.Id
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Seeding failed" });
        }
    }
    [Authorize]
    [HttpPost("verify-password")]
    public async Task<IActionResult> VerifyPassword([FromBody] VerifyPasswordRequest request)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return Unauthorized(new { valid = false });

        var valid = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!valid) return Unauthorized(new { valid = false });

        return Ok(new { valid = true });
    }
}

// Request DTOs for AuthController
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string Token, string RefreshToken);
public record VerifyPasswordRequest(string Password);
