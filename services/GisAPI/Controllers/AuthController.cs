using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Application.Features.Auth.Commands.Impersonate;
using GisAPI.Application.Features.Auth.Commands.Logout;
using GisAPI.Application.Features.Auth.Commands.RefreshToken;
using GisAPI.Application.Features.Users.Commands.ChangeMyPassword;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.DTOs;

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
        var result = await _mediator.Send(new LoginCommand(request.Email, request.Password, GetClientIp(), GetUserAgent()));
        return Ok(result);
    }

    /// <summary>
    /// Inscription libre : crée la société du visiteur ET son compte administrateur,
    /// puis ouvre directement la session (même réponse que la connexion).
    ///
    /// Le chemin doit rester /api/auth/register : l'intercepteur du frontend le
    /// whiteliste explicitement pour ne pas y joindre un jeton périmé, et
    /// PermissionMiddleware laisse passer tout /api/auth. Le renommer déclencherait
    /// une boucle de rafraîchissement puis une déconnexion.
    ///
    /// Le plan et la durée d'essai viennent de la configuration serveur, jamais de
    /// la requête : un champ « plan » ici serait un libre-service.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<GisAPI.Application.Features.Auth.Commands.Register.RegisterResult>> Register(
        [FromBody] RegisterRequest request)
    {
        // 404 et non 403 : sur un déploiement qui ne vend pas l'inscription libre,
        // la route ne doit même pas révéler son existence.
        if (!GisAPI.Domain.Common.AppRegistration.SelfSignupEnabled)
            return NotFound();

        var result = await _mediator.Send(new GisAPI.Application.Features.Auth.Commands.Register.RegisterCommand(
            request.FirstName,
            request.LastName,
            request.Email,
            request.Password,
            request.CompanyName,
            request.Phone,
            request.AccountType ?? GisAPI.Application.Features.Auth.Commands.Register.AccountTypes.Individual,
            request.FleetSizeRange,
            request.Country));

        return Ok(result);
    }

    /// <summary>
    /// Confirmation d'adresse : active un compte issu de l'inscription libre.
    /// Anonyme par nature — l'utilisateur ne peut pas se connecter avant.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("confirm-email")]
    public async Task<ActionResult> ConfirmEmail([FromBody] ConfirmEmailRequest request)
    {
        if (!GisAPI.Domain.Common.AppRegistration.SelfSignupEnabled)
            return NotFound();

        var result = await _mediator.Send(
            new GisAPI.Application.Features.Auth.Commands.ConfirmEmail.ConfirmEmailCommand(request.Token));
        return Ok(result);
    }

    /// <summary>
    /// Renvoi du courriel de confirmation. Répond toujours la même chose, que
    /// l'adresse existe ou non : sinon l'endpoint dirait qui est inscrit chez nous.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("resend-confirmation")]
    public async Task<ActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
    {
        if (!GisAPI.Domain.Common.AppRegistration.SelfSignupEnabled)
            return NotFound();

        var result = await _mediator.Send(
            new GisAPI.Application.Features.Auth.Commands.ResendConfirmation.ResendConfirmationCommand(request.Email));
        return Ok(result);
    }

    /// <summary>
    /// Demande de réinitialisation de mot de passe.
    ///
    /// <para>Répond TOUJOURS 200 avec le même message, que l'adresse soit connue
    /// ou non : distinguer les deux cas transformerait ce point d'entrée en
    /// annuaire d'adresses possédant un compte.</para>
    ///
    /// <para>Contrairement à la confirmation d'inscription, ce point d'entrée
    /// n'est PAS conditionné à l'inscription libre : un utilisateur créé par un
    /// administrateur peut lui aussi perdre son mot de passe, et sur un
    /// déploiement où l'inscription est fermée il n'aurait alors plus aucun
    /// recours.</para>
    /// </summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var result = await _mediator.Send(
            new GisAPI.Application.Features.Auth.Commands.ForgotPassword.ForgotPasswordCommand(request.Email));
        return Ok(result);
    }

    /// <summary>
    /// Pose le nouveau mot de passe à partir du jeton reçu par courriel.
    /// Répond 400 si le jeton est inconnu, périmé ou déjà consommé.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPasswordWithToken([FromBody] ResetPasswordWithTokenRequest request)
    {
        var result = await _mediator.Send(
            new GisAPI.Application.Features.Auth.Commands.ResetPassword.ResetPasswordWithTokenCommand(
                request.Token, request.NewPassword));

        return result.Success ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// "Voir en tant que" : réservé au super-administrateur (system_admin). Renvoie un
    /// jeton au périmètre de l'utilisateur cible pour visualiser l'app comme lui. La
    /// vérification du rôle system_admin est faite dans le handler (ICurrentTenantService).
    /// </summary>
    [Authorize]
    [HttpPost("impersonate")]
    public async Task<ActionResult<LoginResponse>> Impersonate([FromBody] ImpersonateRequest request)
    {
        var result = await _mediator.Send(new ImpersonateCommand(request.UserId));
        return Ok(result);
    }

    public record ImpersonateRequest(int UserId);

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var companyId = int.Parse(User.FindFirst("companyId")?.Value ?? "0");
        await _mediator.Send(new LogoutCommand(userId, companyId, GetClientIp(), GetUserAgent()));
        return NoContent();
    }

    private string? GetClientIp()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private string? GetUserAgent()
    {
        var ua = Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(ua) ? null : ua;
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh([FromBody] RefreshRequest request)
    {
        try
        {
            var result = await _mediator.Send(new RefreshTokenCommand(request.Token, request.RefreshToken, GetClientIp(), GetUserAgent()));
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

    /// <summary>
    /// Self-service password change. Thin wrapper around the
    /// <see cref="ChangeMyPasswordCommand"/> CQRS handler so the frontend
    /// can call <c>POST /api/auth/change-password</c> (alias of
    /// <c>PUT /api/users/me/password</c> on UsersController).
    /// </summary>
    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        await _mediator.Send(new ChangeMyPasswordCommand(
            request.CurrentPassword ?? string.Empty,
            request.NewPassword ?? string.Empty));
        return NoContent();
    }
}

// Request DTOs for AuthController
public record LoginRequest(string Email, string Password);

/// <summary>
/// Corps de l'inscription libre. AUCUN champ de plan d'abonnement : celui-ci vient
/// de la configuration serveur. Le nom de société est facultatif — à défaut, la
/// société prend le nom de la personne.
/// </summary>
public record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? CompanyName,
    string? Phone,
    string? AccountType,
    string? FleetSizeRange = null,
    string? Country = null);

public record ConfirmEmailRequest(string Token);

public record ResendConfirmationRequest(string Email);

public record ForgotPasswordRequest(string Email);
public record ResetPasswordWithTokenRequest(string Token, string NewPassword);
public record RefreshRequest(string Token, string RefreshToken);
public record VerifyPasswordRequest(string Password);
// ChangePasswordRequest is defined in GisAPI.DTOs.AuthDTOs.cs
