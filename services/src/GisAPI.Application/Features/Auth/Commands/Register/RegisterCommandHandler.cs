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
public class RegisterCommandHandler : IRequestHandler<RegisterCommand, RegisterResult>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        ILogger<RegisterCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<RegisterResult> Handle(RegisterCommand request, CancellationToken ct)
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

        // ── La société est créée SILENCIEUSEMENT ──
        //
        // Elle n'est pas une promesse faite à l'utilisateur, c'est la structure sur
        // laquelle repose tout le modèle : filtres de tenant, rôles, abonnement,
        // déclencheur PostgreSQL. Un particulier n'a donc ni à en entendre parler
        // ni à inventer un nom d'entreprise — son espace prend simplement son nom.
        // Le professionnel, lui, donne le nom sous lequel il veut être facturé.
        var isCompany = request.AccountType == AccountTypes.Company;
        var fullName = $"{request.FirstName} {request.LastName}".Trim();
        var societeName = isCompany && !string.IsNullOrWhiteSpace(request.CompanyName)
            ? request.CompanyName!.Trim()
            : (string.IsNullOrWhiteSpace(fullName) ? email : fullName);

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
            // « autre » est la valeur déjà utilisée en base pour ce qui n'est ni du
            // transport ni de la location : un particulier y entre naturellement,
            // sans introduire un type que les écrans existants ne sauraient afficher.
            Type = isCompany ? "transport" : "autre",
            Email = email,
            Phone = request.Phone,
            SubscriptionTypeId = plan.Id,
            IsActive = true,
            SubscriptionStartedAt = now,
            SubscriptionExpiresAt = now.AddDays(AppRegistration.TrialDays),
            BillingCycle = billingCycle,
            SubscriptionStatus = "active",
            NextPaymentAmount = plan.YearlyPrice,
            // Tranche déclarée : conservée telle quelle si elle fait partie des
            // valeurs connues, ignorée sinon. Une tranche fantaisiste vaut moins
            // qu'une absence de tranche — et ne doit pas faire échouer une
            // inscription pour une donnée purement commerciale.
            FleetSizeRange = FleetSizeRanges.IsValid(request.FleetSizeRange)
                ? request.FleetSizeRange
                : null,
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
            // « pending » et non « active » : le compte n'est utilisable qu'une fois
            // l'adresse confirmée. Aucune garde supplémentaire n'est nécessaire, la
            // connexion refuse déjà tout statut différent de « active ».
            Status = "pending",
            EmailVerificationToken = GenerateToken(),
            EmailVerificationExpiresAt = now.AddHours(AppRegistration.EmailConfirmationHours),
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

        _logger.LogInformation(
            "Inscription libre : espace « {Company} » (#{Id}) créé par {Email} ({Type}), plan {Plan}, essai jusqu'au {Expiry:yyyy-MM-dd}",
            societe.Name, societe.Id, user.Email, request.AccountType, plan.Code, societe.SubscriptionExpiresAt);

        // ── Courriel de confirmation ──
        //
        // AUCUNE session n'est ouverte ici : renvoyer un jeton viderait la
        // confirmation de son sens. L'utilisateur reçoit un lien, le suit, et son
        // compte bascule en « active ».
        //
        // L'envoi est BORNÉ DANS LE TEMPS et ne peut pas retenir la réponse. Sans
        // cela, l'inscription reste suspendue tant que le relais SMTP ne répond
        // pas : le compte est créé, mais l'écran tourne indéfiniment sur
        // « Création du compte… » et l'utilisateur croit l'application plantée —
        // c'est exactement ce qui s'est produit en conditions réelles. À noter que
        // SmtpClient.Timeout ne s'applique QU'À l'envoi synchrone : sur
        // SendMailAsync il est ignoré, rien ne borne l'appel par défaut.
        var delivered = await TrySendConfirmationEmailWithinAsync(
            user, societe.Name, TimeSpan.FromSeconds(8));

        return new RegisterResult(
            user.Email,
            delivered
                ? "Votre compte est créé. Ouvrez le lien de confirmation que nous venons de vous envoyer par email."
                : "Votre compte est créé. L'email de confirmation peut mettre quelques minutes à arriver — "
                  + "s'il ne vient pas, demandez-en un nouveau.",
            delivered);
    }

    /// <summary>Jeton d'activation : aléatoire cryptographique, sûr en URL.</summary>
    private static string GenerateToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Envoie le courriel de confirmation SANS jamais retenir la réponse au-delà de
    /// <paramref name="budget"/>. Au-delà, l'envoi se poursuit en arrière-plan (il
    /// journalise lui-même son issue) et l'inscription rend la main.
    ///
    /// Le jeton d'annulation de la requête n'est VOLONTAIREMENT pas propagé : un
    /// utilisateur qui ferme son onglet ne doit pas annuler l'email qui lui est
    /// destiné.
    ///
    /// Retourne vrai seulement si l'envoi s'est achevé dans le délai. Ce n'est pas
    /// une preuve de RÉCEPTION : le service de messagerie absorbe ses propres
    /// erreurs, il n'y a donc pas de moyen ici de distinguer « remis » de « refusé
    /// par le relais ». D'où une formulation prudente côté écran, et un bouton de
    /// renvoi toujours disponible.
    /// </summary>
    private async Task<bool> TrySendConfirmationEmailWithinAsync(User user, string spaceName, TimeSpan budget)
    {
        using var cts = new CancellationTokenSource(budget);
        var send = TrySendConfirmationEmailAsync(user, spaceName, cts.Token);

        var finished = await Task.WhenAny(send, Task.Delay(budget));
        if (finished != send)
        {
            _logger.LogWarning(
                "Courriel de confirmation non parti en moins de {Seconds}s pour {Email} — envoi poursuivi en arrière-plan.",
                budget.TotalSeconds, user.Email);
            return false;
        }

        return await send;
    }

    private async Task<bool> TrySendConfirmationEmailAsync(User user, string spaceName, CancellationToken ct)
    {
        try
        {
            var link = $"{AppUrls.PublicBaseUrl}/confirmation-email?token={user.EmailVerificationToken}";
            var hours = AppRegistration.EmailConfirmationHours;

            var html = $@"
<div style=""font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:560px;margin:0 auto;color:#0f172a;"">
  <h2 style=""font-size:20px;margin:0 0 14px;"">Confirmez votre adresse email</h2>
  <p style=""font-size:14.5px;line-height:1.6;color:#334155;margin:0 0 20px;"">
    Bonjour {System.Net.WebUtility.HtmlEncode(user.FirstName)},<br/>
    votre espace <strong>{System.Net.WebUtility.HtmlEncode(spaceName)}</strong> est prêt.
    Il ne reste qu'à confirmer cette adresse pour vous connecter.
  </p>
  <p style=""margin:0 0 24px;"">
    <a href=""{link}"" style=""display:inline-block;padding:12px 28px;background:#4f46e5;color:#fff;text-decoration:none;border-radius:8px;font-weight:600;font-size:14px;"">Confirmer mon adresse</a>
  </p>
  <p style=""font-size:12.5px;line-height:1.6;color:#64748b;margin:0 0 8px;"">
    Ce lien est valable {hours} heures. Si le bouton ne fonctionne pas, copiez cette adresse dans votre navigateur :<br/>
    <span style=""word-break:break-all;color:#4f46e5;"">{link}</span>
  </p>
  <p style=""font-size:12.5px;color:#94a3b8;margin:18px 0 0;"">
    Vous n'êtes pas à l'origine de cette inscription ? Ignorez ce message, le compte restera inactif.
  </p>
</div>";

            // throwOnFailure : sans lui, le service absorbe ses erreurs et on
            // annoncerait « email envoyé » alors que le relais est injoignable.
            await _emailService.SendEmailAsync(
                user.Email,
                $"{user.FirstName} {user.LastName}".Trim(),
                "Confirmez votre adresse email",
                html,
                ct,
                throwOnFailure: true);
            return true;
        }
        catch (Exception ex)
        {
            // Ne jamais faire échouer l'inscription sur un incident de messagerie :
            // le compte existe, il est confirmable par un renvoi.
            _logger.LogError(ex, "Envoi du courriel de confirmation impossible pour {Email}", user.Email);
            return false;
        }
    }
}
