using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Register;

/// <summary>
/// Inscription libre : un visiteur crée son compte, et donc SA SOCIÉTÉ.
///
/// POURQUOI UNE SOCIÉTÉ — toute la plateforme est cloisonnée par société : les
/// filtres globaux de GisDbContext, les rôles, les droits d'abonnement, jusqu'au
/// déclencheur PostgreSQL qui impose au rôle et à l'utilisateur d'appartenir à la
/// même société. Un utilisateur sans société n'est pas un utilisateur dégradé,
/// c'est un utilisateur qui ne peut rien faire. L'inscription crée donc, dans
/// l'ordre : la société, ses rôles, puis l'utilisateur fondateur.
///
/// Ce handler existait déjà mais n'était exposé par aucun endpoint : jamais
/// exécuté, il avait accumulé plusieurs défauts qui n'auraient été découverts
/// qu'en production. Ils sont corrigés ici et commentés sur place.
/// </summary>
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        ILogger<RegisterCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(RegisterCommand request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ConflictException("Cet email est déjà utilisé");

        // ── Le plan est résolu par CODE, depuis la configuration du déploiement ──
        //
        // L'ancien code prenait `FirstOrDefaultAsync(st => st.IsActive)` : sans
        // ORDER BY, le plan servi n'était pas déterministe, et `?.Id` tolérait
        // l'absence totale de plan. Une société sans abonnement échappe pourtant
        // à TOUT contrôle de droits (PermissionMiddleware ne filtre rien quand le
        // plan est null) : c'est le trou qu'on vient de fermer côté création
        // administrateur, il n'a aucune raison de rester ouvert ici.
        //
        // Le plan ne vient PAS de la requête : le laisser choisir au visiteur,
        // c'est lui laisser s'offrir le plan le plus complet.
        var plan = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(st => st.Code == AppRegistration.DefaultPlanCode && st.IsActive, ct);

        if (plan == null)
        {
            _logger.LogError(
                "Inscription impossible : le plan « {Code} » est introuvable ou inactif.",
                AppRegistration.DefaultPlanCode);
            throw new DomainException("L'inscription est momentanément indisponible. Réessayez plus tard.");
        }

        // Une société « au nom de l'utilisateur » : le nom d'entreprise est
        // facultatif, beaucoup de nouveaux venus n'en ont pas encore.
        var fullName = $"{request.FirstName} {request.LastName}".Trim();
        var societeName = string.IsNullOrWhiteSpace(request.CompanyName)
            ? (string.IsNullOrWhiteSpace(fullName) ? email : fullName)
            : request.CompanyName.Trim();

        var now = DateTime.UtcNow;
        const string billingCycle = "yearly";

        // ── Colonnes d'abonnement : un essai BORNÉ, jamais NULL ──
        //
        // Sans SubscriptionExpiresAt, SubscriptionPolicy retourne « active » pour
        // toujours : le compte n'est jamais bloqué, n'affiche aucune bannière et
        // n'apparaît même pas dans la supervision (qui filtre sur une échéance non
        // nulle). L'inscription offrirait alors un accès gratuit et perpétuel, et
        // l'écran de paiement n'aurait rien à faire respecter.
        //
        // Le statut reste « active » : les écrans d'administration filtrent sur
        // cette valeur exacte, un statut « trial » inventé les ferait disparaître.
        // C'est la date d'échéance qui porte la notion d'essai.
        var societe = new Societe
        {
            Name = societeName,
            Type = "transport",
            Email = email,
            Phone = request.Phone,
            SubscriptionTypeId = plan.Id,
            IsActive = true,
            SubscriptionStartedAt = now,
            SubscriptionExpiresAt = now.AddDays(AppRegistration.TrialDays),
            BillingCycle = billingCycle,
            SubscriptionStatus = "active",
            NextPaymentAmount = plan.YearlyPrice,
            Settings = new SocieteSettings()
        };
        _context.Societes.Add(societe);
        await _context.SaveChangesAsync(ct);

        // ── Rôles ──
        //
        // Les permissions sont écrites à PLAT (une clé par module), forme lue par
        // le reste de l'application. L'ancien code produisait un dictionnaire
        // imbriqué { "modules": { ... } } que personne ne sait lire.
        //
        // L'ordre société → rôles → utilisateur est imposé par un déclencheur
        // PostgreSQL (rôle et utilisateur dans la même société) : le conserver.
        var adminRole = new Role
        {
            Name = "Administrateur",
            Description = "Administrateur de la société avec tous les droits",
            SocieteId = societe.Id,
            IsCompanyAdmin = true,
            IsSystemRole = false,
            Permissions = new Dictionary<string, object>
            {
                { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                { "employees", true }, { "maintenance", true }, { "costs", true },
                { "reports", true }, { "geofences", true }, { "settings", true },
                { "users", true }, { "suppliers", true }, { "documents", true },
                { "accidents", true }, { "fleet_management", true }
            }
        };
        _context.Roles.Add(adminRole);

        // Un rôle non-admin dès le départ : sans lui, la création du premier
        // employé n'aurait aucun rôle à lui donner.
        var operatorRole = new Role
        {
            Name = "Opérateur",
            Description = "Utilisateur avec accès limité",
            SocieteId = societe.Id,
            IsCompanyAdmin = false,
            IsSystemRole = false,
            Permissions = new Dictionary<string, object>
            {
                { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                { "maintenance", true }, { "documents", true }
            }
        };
        _context.Roles.Add(operatorRole);
        await _context.SaveChangesAsync(ct);

        // ── Utilisateur fondateur ──
        //
        // AccessLevel « admin » et tous les Can* à vrai : les valeurs par défaut de
        // l'entité laissent plusieurs droits à faux (CanReports notamment), si bien
        // que le créateur de la société se serait retrouvé bridé sur ses propres
        // données.
        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = email,
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            CompanyId = societe.Id,
            RoleId = adminRole.Id,
            Status = "active",
            AccessLevel = "admin",
            CanMonitoring = true, CanVehicles = true, CanDrivers = true,
            CanReports = true, CanGeofences = true, CanMaintenance = true,
            CanCosts = true, CanFuel = true, CanDocuments = true,
            CanAccidents = true, CanUsers = true, CanSettings = true,
            CanSuppliers = true, CanFleetManagement = true, CanTours = true,
            CanPlayback = true,
            CanReportTrips = true, CanReportFuel = true, CanReportSpeed = true,
            CanReportStops = true, CanReportMileage = true, CanReportCosts = true,
            CanReportMaintenance = true, CanReportDaily = true, CanReportMonthly = true,
            CanReportMileagePeriod = true, CanReportSpeedInfraction = true,
            CanReportDrivingBehavior = true, CanReportMonthlyCosts = true
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        // ── Session ──
        //
        // Le jeton de rafraîchissement doit être PERSISTÉ. L'ancien code en
        // renvoyait un au client sans jamais l'enregistrer : au premier
        // renouvellement — donc quelques minutes après l'inscription — le serveur
        // ne le reconnaissait pas et le nouvel inscrit était déconnecté.
        var token = _jwtService.GenerateToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();
        // Nom complet : « RefreshToken » désigne aussi un espace de noms de
        // commandes dans cette couche.
        _context.RefreshTokens.Add(new GisAPI.Domain.Entities.RefreshToken
        {
            Token = refreshTokenStr,
            UserId = user.Id,
            ExpiresAt = now.AddDays(7),
            CreatedAt = now
        });
        await _context.SaveChangesAsync(ct);

        // Renseigner les navigations avant de construire la réponse, sinon le nom
        // de société et la devise sortent vides.
        user.Role = adminRole;
        user.Societe = societe;
        societe.SubscriptionType = plan;

        _logger.LogInformation(
            "Inscription libre : société « {Company} » (#{Id}) créée par {Email}, plan {Plan}, essai jusqu'au {Expiry:yyyy-MM-dd}",
            societe.Name, societe.Id, user.Email, plan.Code, societe.SubscriptionExpiresAt);

        // On réutilise les constructeurs du login : une réponse d'inscription qui
        // aurait sa propre forme finirait par diverger de celle de la connexion.
        return new LoginResponse(
            token,
            refreshTokenStr,
            LoginCommandHandler.BuildUserDto(
                user,
                LoginCommandHandler.BuildSubscriptionFeatures(plan),
                Array.Empty<int>(),
                LoginCommandHandler.BuildUserPermissions(user))
        );
    }
}
