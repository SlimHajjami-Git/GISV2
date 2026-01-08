using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GisAPI.Infrastructure.Persistence;
using GisAPI.DTOs;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(GisDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await _context.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Email ou mot de passe incorrect" });
        }

        if (user.Status != "active")
        {
            return Unauthorized(new { message = "Compte désactivé" });
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        return Ok(new AuthResponse(
            token,
            refreshToken,
            new UserDto(
                user.Id,
                user.Name,
                user.Email,
                user.Phone,
                user.Roles,
                user.Permissions,
                user.CompanyId,
                user.Company?.Name ?? ""
            )
        ));
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { message = "Cet email est déjà utilisé" });
        }

        // Get default subscription
        var subscription = await _context.Subscriptions.FirstOrDefaultAsync();
        if (subscription == null)
        {
            subscription = new Subscription
            {
                Name = "Plan Gratuit",
                Type = "parc",
                Price = 0,
                MaxVehicles = 5,
                GpsTracking = false,
                GpsInstallation = false
            };
            _context.Subscriptions.Add(subscription);
            await _context.SaveChangesAsync();
        }

        // Create company
        var company = new Company
        {
            Name = request.CompanyName,
            Type = "transport",
            SubscriptionId = subscription.Id
        };
        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        // Create admin user
        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Roles = new[] { "admin" },
            Permissions = new[] { "dashboard", "monitoring", "vehicles", "drivers", "geofences", "reports", "maintenance", "costs", "gps-devices", "users", "settings" },
            CompanyId = company.Id,
            Status = "active"
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);
        var refreshToken = GenerateRefreshToken();

        return Ok(new AuthResponse(
            token,
            refreshToken,
            new UserDto(
                user.Id,
                user.Name,
                user.Email,
                user.Phone,
                user.Roles,
                user.Permissions,
                user.CompanyId,
                company.Name
            )
        ));
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _configuration["Jwt:Key"] ?? "DefaultSecretKeyForDevelopment123!"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new("companyId", user.CompanyId.ToString())
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "GisAPI",
            audience: _configuration["Jwt:Audience"] ?? "GisAPI",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    [HttpPost("seed")]
    public async Task<ActionResult> SeedDatabase([FromQuery] string? secret = null)
    {
        // Simple security check - require a secret or only allow in Development
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (environment != "Development" && secret != "CalypsoSeed2026")
        {
            return Unauthorized(new { message = "Seeding only allowed in Development or with correct secret" });
        }

        try
        {
            // Check if data already exists
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == "admin@belive.ma");
            if (existingUser != null)
            {
                // Update password for existing user
                existingUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Calypso@2026+");
                existingUser.Status = "active";
                existingUser.Roles = new[] { "superadmin" };
                existingUser.Permissions = new[] { "all" };
                await _context.SaveChangesAsync();
                return Ok(new { message = "User admin@belive.ma updated", password = "Calypso@2026+" });
            }

            // Create subscription
            var subscription = await _context.Subscriptions.FirstOrDefaultAsync();
            if (subscription == null)
            {
                subscription = new Subscription
                {
                    Name = "Plan Pro",
                    Type = "parc_gps",
                    Price = 999.00m,
                    Features = new[] { "gps_tracking", "geofencing", "reports", "alerts", "maintenance" },
                    GpsTracking = true,
                    GpsInstallation = true,
                    MaxVehicles = 100,
                    MaxUsers = 20,
                    MaxGpsDevices = 100,
                    MaxGeofences = 50,
                    BillingCycle = "monthly",
                    IsActive = true
                };
                _context.Subscriptions.Add(subscription);
                await _context.SaveChangesAsync();
            }

            // Create company
            var company = await _context.Companies.FirstOrDefaultAsync(c => c.Name == "Belive");
            if (company == null)
            {
                company = new Company
                {
                    Name = "Belive",
                    Type = "transport",
                    Address = "Sfax, Tunisie",
                    City = "Sfax",
                    Country = "TN",
                    Phone = "+216 74 000 000",
                    Email = "contact@belive.tn",
                    SubscriptionId = subscription.Id,
                    IsActive = true,
                    SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
                };
                _context.Companies.Add(company);
                await _context.SaveChangesAsync();
            }

            // Create admin user
            var adminUser = new User
            {
                Name = "Admin Belive",
                Email = "admin@belive.ma",
                Phone = "+216 00 000 000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Calypso@2026+"),
                Roles = new[] { "superadmin" },
                Permissions = new[] { "all" },
                AssignedVehicleIds = Array.Empty<int>(),
                Status = "active",
                CompanyId = company.Id
            };
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Database seeded successfully",
                email = "admin@belive.ma",
                password = "Calypso@2026+",
                companyId = company.Id
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = "Seeding failed", error = ex.Message });
        }
    }
}
