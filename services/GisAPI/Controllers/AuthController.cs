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
            .Include(u => u.Societe)
            .Include(u => u.Role)
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
                user.Societe?.Name ?? ""
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

        // Get default subscription type
        var subscriptionType = await _context.SubscriptionTypes.FirstOrDefaultAsync();
        if (subscriptionType == null)
        {
            subscriptionType = new SubscriptionType
            {
                Name = "Plan Gratuit",
                Code = "plan-gratuit",
                TargetCompanyType = "all",
                YearlyPrice = 0,
                MaxVehicles = 5,
                GpsTracking = false,
                GpsInstallation = false,
                IsActive = true
            };
            _context.SubscriptionTypes.Add(subscriptionType);
            await _context.SaveChangesAsync();
        }

        // Create company
        var company = new Societe
        {
            Name = request.CompanyName,
            Type = "transport",
            SubscriptionTypeId = subscriptionType.Id
        };
        _context.Societes.Add(company);
        await _context.SaveChangesAsync();

        // Create admin role for the company
        var adminRole = new Role
        {
            Name = "Administrateur",
            Description = "Administrateur avec tous les accès",
            SocieteId = company.Id,
            IsCompanyAdmin = true,
            Permissions = new Dictionary<string, object>
            {
                { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                { "drivers", true }, { "geofences", true }, { "reports", true },
                { "maintenance", true }, { "costs", true }, { "gps_devices", true },
                { "users", true }, { "settings", true }
            }
        };
        _context.Roles.Add(adminRole);
        await _context.SaveChangesAsync();

        // Create admin user
        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = adminRole.Id,
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
            new("companyId", user.CompanyId.ToString()),
            new("userType", user.UserType ?? "user")
        };

        // Add role name as claim
        if (user.Role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            
            // System admin gets super_admin role for backward compatibility
            if (user.Role.IsSystemRole)
            {
                claims.Add(new Claim(ClaimTypes.Role, "super_admin"));
                claims.Add(new Claim(ClaimTypes.Role, "system_admin"));
            }
            
            // Company admin gets admin role
            if (user.Role.IsCompanyAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "admin"));
                claims.Add(new Claim(ClaimTypes.Role, "company_admin"));
            }
            
            // Add permissions from role
            if (user.Role.Permissions != null)
            {
                foreach (var permission in user.Role.Permissions.Keys)
                {
                    claims.Add(new Claim("permission", permission));
                }
            }
        }

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "GisAPI",
            audience: _configuration["Jwt:Audience"] ?? "GisAPI",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
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
                // Role is already assigned via role_id
                await _context.SaveChangesAsync();
                return Ok(new { message = "User admin@belive.ma updated", password = "Calypso@2026+" });
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
                    IsActive = true
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
