using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        ILogger<LoginCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);

        // Le motif journalisé est précis, la réponse ne l'est pas : « adresse inconnue »
        // et « mot de passe incorrect » gardent le même message, sinon la connexion
        // dirait qui possède un compte.
        if (user == null)
        {
            await RecordFailedLoginAsync(request, null, "adresse inconnue", ct);
            throw new DomainException("Email ou mot de passe incorrect");
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            await RecordFailedLoginAsync(request, user, "mot de passe incorrect", ct);
            throw new DomainException("Email ou mot de passe incorrect");
        }

        // Un compte en attente de confirmation n'est pas un compte désactivé : dire
        // « Compte désactivé » à quelqu'un qui vient de s'inscrire l'envoie chercher
        // un administrateur au lieu d'ouvrir son courrier.
        if (user.Status == "pending")
        {
            await RecordFailedLoginAsync(request, user, "adresse non confirmée", ct);
            throw new DomainException(
                "Votre adresse email n'est pas encore confirmée. Ouvrez le lien reçu par email, "
                + "ou demandez-en un nouveau.");
        }

        if (user.Status != "active")
        {
            await RecordFailedLoginAsync(request, user, "compte désactivé", ct);
            throw new DomainException("Compte désactivé");
        }

        // Compte chauffeur (migration 050) : application mobile SEULEMENT. Le site ne se
        // déclare jamais comme appelant ; c'est ce refus, avec la liste blanche du
        // PermissionMiddleware, qui fait qu'un chauffeur ne voit que ses tournées.
        if (user.IsDriverAccount && !LoginClients.IsMobile(request.ClientType))
        {
            await RecordFailedLoginAsync(request, user, "compte chauffeur hors application mobile", ct);
            throw new DomainException(LoginClients.DriverWebLoginRefused);
        }

        // Société suspendue ou expirée au-delà de la grâce : pas de nouveau token
        // (le sys_admin plateforme, lui, doit toujours pouvoir se connecter).
        if (user.Societe != null && user.Role?.IsSystemRole != true)
        {
            var subState = Common.SubscriptionPolicy.Evaluate(user.Societe, DateTime.UtcNow);
            if (subState.IsBlocked)
            {
                var expired = subState.Reason == "expired";
                await RecordFailedLoginAsync(request, user,
                    expired ? "abonnement expiré" : "abonnement suspendu", ct);
                throw new DomainException(expired
                    ? "L'abonnement de votre société a expiré. Contactez votre prestataire pour le renouveler."
                    : "L'abonnement de votre société est suspendu. Contactez votre prestataire.");
            }
        }

        // Explicitly load SubscriptionType if Societe has one
        if (user.Societe?.SubscriptionTypeId != null)
        {
            user.Societe.SubscriptionType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == user.Societe.SubscriptionTypeId, ct);
        }

        // Lu AVANT d'écrire la date du jour : vide, c'est la toute première connexion.
        var firstLogin = user.LastLoginAt == null;
        user.LastLoginAt = DateTime.UtcNow;

        var token = _jwtService.GenerateToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();

        // Revoke any existing active refresh tokens for this user
        var existingTokens = _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow);
        foreach (var existing in existingTokens)
            existing.RevokedAt = DateTime.UtcNow;

        // Save new refresh token to DB
        var refreshTokenEntity = new GisAPI.Domain.Entities.RefreshToken
        {
            Token = refreshTokenStr,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenLifetime.DaysFor(user)),
            CreatedAt = DateTime.UtcNow
        };
        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(ct);

        // Best-effort: record the login in the audit trail. Never break auth if this fails.
        try
        {
            _context.AuditLogs.Add(new GisAPI.Domain.Entities.AuditLog
            {
                UserId = user.Id,
                CompanyId = user.CompanyId,
                Action = "login",
                EntityType = "User",
                EntityId = user.Id,
                EntityName = user.Email,
                Description = "Connexion utilisateur",
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write login audit log for {Email}", user.Email);
        }

        // Build subscription features
        var subscriptionFeatures = BuildSubscriptionFeatures(user.Societe?.SubscriptionType);

        // Load assigned vehicle IDs for non-admin users
        int[]? assignedVehicleIds = null;
        if (user.Role != null && !user.Role.IsCompanyAdmin && !user.Role.IsSystemRole)
        {
            assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == user.Id)
                .Select(uv => uv.VehicleId)
                .ToArrayAsync(ct);
        }

        _logger.LogInformation("User {Email} logged in (CompanyId: {CompanyId})", user.Email, user.CompanyId);

        var userPermissions = BuildUserPermissions(user);

        return new LoginResponse(
            token,
            refreshTokenStr,
            BuildUserDto(user, subscriptionFeatures, assignedVehicleIds, userPermissions),
            FirstLogin: firstLogin
        );
    }

    /// <summary>Action inscrite dans audit_logs pour chaque connexion refusée.</summary>
    public const string FailedLoginAction = "login_failed";

    /// <summary>
    /// Constat DEF-030 : un refus de connexion ne laissait aucune trace, ni journal
    /// applicatif exploitable (une erreur générique sans adresse ni IP), ni ligne
    /// d'audit — une force brute passait inaperçue. Chaque refus est désormais écrit
    /// dans les deux, à côté des lignes « login » / « logout » / « session » existantes.
    ///
    /// Adresse MASQUÉE dans le journal applicatif : il part vers la sortie standard
    /// du pod et ses collecteurs, et le champ e-mail reçoit souvent un mot de passe
    /// tapé au mauvais endroit. Pour une adresse inconnue, même masque dans l'audit ;
    /// pour un compte existant, l'audit garde l'adresse du compte, comme la connexion
    /// réussie. Écriture best-effort : un audit en échec ne change pas la réponse.
    /// </summary>
    private async Task RecordFailedLoginAsync(
        LoginCommand request, GisAPI.Domain.Entities.User? user, string motif, CancellationToken ct)
    {
        var maskedEmail = MaskEmail(request.Email);
        _logger.LogWarning("Échec de connexion pour {Email} depuis {IpAddress} : {Motif}",
            maskedEmail, request.IpAddress ?? "adresse inconnue", motif);

        try
        {
            _context.AuditLogs.Add(new GisAPI.Domain.Entities.AuditLog
            {
                // UserId reste vide : FK_audit_logs_users_UserId est en NO ACTION et les
                // suppressions d'utilisateur ne purgent pas audit_logs, un seul mot de passe
                // mal tapé rendait le compte insupprimable (500). EntityId et CompanyId, sans
                // clé étrangère, suffisent à l'administrateur pour retrouver le compte visé.
                UserId = null,
                CompanyId = user?.CompanyId,
                Action = FailedLoginAction,
                EntityType = "User",
                EntityId = user?.Id,
                EntityName = user?.Email ?? maskedEmail,
                Description = $"Échec de connexion : {motif}",
                IpAddress = request.IpAddress,
                // En-tête fourni par l'appelant, donc par l'attaquant : borné comme dans
                // AuditTrailMiddleware pour qu'une boucle ne gonfle pas la table.
                UserAgent = request.UserAgent is { Length: > 250 } ua ? ua[..250] : request.UserAgent,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de connexion non inscrit au journal d'audit ({Email})", maskedEmail);
        }
    }

    /// <summary>
    /// « karim.hajjami@gmail.com » → « ka***@gmail.com ». Le domaine reste lisible :
    /// il suffit à voir quelle société est visée, sans exposer l'identifiant.
    ///
    /// Tout ce qui n'a pas la forme d'une adresse devient « *** », sans rien de visible :
    /// le validateur laisse passer « Motdepasse@2026 » (un seul @, ni au début ni à la
    /// fin), et en garder le début et la fin publiait un fragment de mot de passe tapé
    /// dans le mauvais champ.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        // Minuscules comme la recherche du compte : deux essais sur la même adresse
        // se regroupent dans les journaux quelle que soit la casse tapée.
        var value = email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length == 0) return "(vide)";

        // 254 caractères, longueur maximale d'une adresse : au-delà ce n'en est pas une,
        // et l'expression ne tourne jamais sur une saisie démesurée.
        var at = value.LastIndexOf('@');
        if (at <= 0 || value.Length > 254 || !PlausibleDomain.IsMatch(value[at..]))
            return "***";

        var local = value[..at];
        var visible = local.Length > 4 ? local[..2] : local[..1];
        var masked = visible + "***" + value[at..];

        // Une adresse très longue ne doit pas se recopier telle quelle dans les journaux.
        return masked.Length > 80 ? masked[..80] : masked;
    }

    // Au moins un point et une extension alphabétique : « @gmail.com » oui, « @2026 » non.
    private static readonly System.Text.RegularExpressions.Regex PlausibleDomain = new(
        @"^@([a-z0-9-]+\.)+[a-z]{2,}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>Construit le UserDto renvoyé par le login (réutilisé par l'impersonation).</summary>
    public static UserDto BuildUserDto(
        GisAPI.Domain.Entities.User user,
        SubscriptionFeaturesDto? subscriptionFeatures,
        int[]? assignedVehicleIds,
        UserPermissionsDto userPermissions)
    {
        return new UserDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Phone,
            user.PermitNumber,
            user.RoleId,
            user.Role?.Name ?? "",
            user.Role?.IsCompanyAdmin ?? false,
            user.Role?.IsSystemAdmin ?? false,
            user.CompanyId,
            user.Societe?.Name ?? "",
            user.Societe?.Type,
            user.Role?.Permissions,
            subscriptionFeatures,
            assignedVehicleIds,
            userPermissions,
            Currency: user.Societe?.Settings?.Currency ?? GisAPI.Domain.Common.AppCurrency.Default,
            SelfServiceSubscription: IsSelfServiceSubscription(user.Societe)
        )
        { AccountType = user.AccountType };
    }

    /// <summary>
    /// La société gère-t-elle son abonnement elle-même ?
    ///
    /// UN SEUL critère : être sur l'offre de gestion de parc en libre-service
    /// (plan-basique). Les comptes issus de l'inscription libre y atterrissent
    /// tous (Registration:DefaultPlanCode) — la période d'essai est donc couverte
    /// par le même critère, sans en avoir besoin d'un second.
    ///
    /// La première version ajoutait « jamais réglé = en essai »
    /// (last_payment_at nul). Faux en pratique : les règlements des clients
    /// installés se font HORS application — virement, chèque, espèces — et
    /// last_payment_at n'est renseigné pour AUCUNE des 13 sociétés de la
    /// production. Le critère rendait donc l'écran de paiement visible à tout le
    /// monde, exactement ce qu'il devait empêcher.
    ///
    /// Tous les autres plans — abonnement négocié puis facturé à la main — ne
    /// voient pas l'écran : il est inactif, et laisser croire qu'on peut changer
    /// de formule d'un clic ne ferait que créer des appels au support.
    /// </summary>
    public static bool IsSelfServiceSubscription(GisAPI.Domain.Entities.Societe? societe)
    {
        return societe?.SubscriptionType?.Code == "plan-basique";
    }

    /// <summary>Map les permissions utilisateur (réutilisé par l'impersonation).</summary>
    public static UserPermissionsDto BuildUserPermissions(GisAPI.Domain.Entities.User user)
    {
        return new UserPermissionsDto(
            AccessLevel: user.AccessLevel,
            CanMonitoring: user.CanMonitoring,
            CanVehicles: user.CanVehicles,
            CanDrivers: user.CanDrivers,
            CanReports: user.CanReports,
            CanGeofences: user.CanGeofences,
            CanMaintenance: user.CanMaintenance,
            CanCosts: user.CanCosts,
            CanFuel: user.CanFuel,
            CanDocuments: user.CanDocuments,
            CanAccidents: user.CanAccidents,
            CanUsers: user.CanUsers,
            CanSettings: user.CanSettings,
            CanSuppliers: user.CanSuppliers,
            CanFleetManagement: user.CanFleetManagement,
            CanTours: user.CanTours,
            CanPlayback: user.CanPlayback,
            CanReportTrips: user.CanReportTrips,
            CanReportFuel: user.CanReportFuel,
            CanReportSpeed: user.CanReportSpeed,
            CanReportStops: user.CanReportStops,
            CanReportMileage: user.CanReportMileage,
            CanReportCosts: user.CanReportCosts,
            CanReportMaintenance: user.CanReportMaintenance,
            CanReportDaily: user.CanReportDaily,
            CanReportMonthly: user.CanReportMonthly,
            CanReportMileagePeriod: user.CanReportMileagePeriod,
            CanReportSpeedInfraction: user.CanReportSpeedInfraction,
            CanReportDrivingBehavior: user.CanReportDrivingBehavior,
            CanReportMonthlyCosts: user.CanReportMonthlyCosts,
            CanReportOperatingCost: user.CanReportOperatingCost,
            CanReportCostEvolution: user.CanReportCostEvolution,
            CanReportCostRanking: user.CanReportCostRanking,
            CanReportRepairFrequency: user.CanReportRepairFrequency,
            CanReportMonthlyFuel: user.CanReportMonthlyFuel,
            CanReportAiFleet: user.CanReportAiFleet,
            CanReportFuelEstimation: user.CanReportFuelEstimation,
            CanReportFuelComparison: user.CanReportFuelComparison
        );
    }

    public static SubscriptionFeaturesDto? BuildSubscriptionFeatures(
        GisAPI.Domain.Entities.SubscriptionType? subType)
    {
        if (subType == null) return null;

        return new SubscriptionFeaturesDto(
            GpsTracking: subType.GpsTracking,
            GpsInstallation: subType.GpsInstallation,
            ApiAccess: subType.ApiAccess,
            AdvancedReports: subType.AdvancedReports,
            RealTimeAlerts: subType.RealTimeAlerts,
            HistoryPlayback: subType.HistoryPlayback,
            FuelAnalysis: subType.FuelAnalysis,
            DrivingBehavior: subType.DrivingBehavior,
            ModuleDashboard: subType.ModuleDashboard,
            ModuleMonitoring: subType.ModuleMonitoring,
            ModuleVehicles: subType.ModuleVehicles,
            ModuleEmployees: subType.ModuleEmployees,
            ModuleGeofences: subType.ModuleGeofences,
            ModuleMaintenance: subType.ModuleMaintenance,
            ModuleCosts: subType.ModuleCosts,
            ModuleFuel: subType.ModuleFuel,
            ModuleReports: subType.ModuleReports,
            ModuleSettings: subType.ModuleSettings,
            ModuleUsers: subType.ModuleUsers,
            ModuleSuppliers: subType.ModuleSuppliers,
            ModuleDocuments: subType.ModuleDocuments,
            ModuleAccidents: subType.ModuleAccidents,
            ModuleFleetManagement: subType.ModuleFleetManagement,
            ModuleTours: subType.ModuleTours,
            ReportTrips: subType.ReportTrips,
            ReportFuel: subType.ReportFuel,
            ReportSpeed: subType.ReportSpeed,
            ReportStops: subType.ReportStops,
            ReportMileage: subType.ReportMileage,
            ReportCosts: subType.ReportCosts,
            ReportMaintenance: subType.ReportMaintenance,
            ReportDaily: subType.ReportDaily,
            ReportMonthly: subType.ReportMonthly,
            ReportMileagePeriod: subType.ReportMileagePeriod,
            ReportSpeedInfraction: subType.ReportSpeedInfraction,
            ReportDrivingBehavior: subType.ReportDrivingBehavior,
            ReportMonthlyCosts: subType.ReportMonthlyCosts,
            MaxVehicles: subType.MaxVehicles,
            MaxUsers: subType.MaxUsers,
            MaxGpsDevices: subType.MaxGpsDevices,
            MaxGeofences: subType.MaxGeofences,
            HistoryRetentionDays: subType.HistoryRetentionDays
        );
    }
}



