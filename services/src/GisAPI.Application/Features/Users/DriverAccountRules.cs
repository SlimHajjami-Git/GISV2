using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users;

/// <summary>
/// Ce qu'est un compte CHAUFFEUR (users.account_type = driver, migration 050, décision de
/// Slim du 21/09/2026 : « le chauffeur est un utilisateur »). Un administrateur le crée
/// dans l'écran Utilisateurs en cochant « Chauffeur » ; il se connecte à l'application
/// mobile avec son e-mail et son mot de passe, et n'y voit que ses tournées.
///
/// Une seule définition pour la création et la modification : ce compte n'a AUCUN droit
/// de gestion (toutes les cases à false, jamais administrateur, aucune affectation de
/// véhicule — il recevrait sinon les alertes de la flotte), et il est relié à une fiche
/// <see cref="Driver"/>, qui porte le véhicule affecté, le permis et les tournées.
/// </summary>
public static class DriverAccountRules
{
    /// <summary>Applique au compte tout ce qu'un chauffeur n'a pas le droit d'avoir.</summary>
    public static void Apply(User user)
    {
        user.AccountType = UserAccountTypes.Driver;
        user.AccessLevel = "user";
        user.EmployeeRole = "driver";
        user.CanMonitoring = false;
        user.CanVehicles = false;
        user.CanDrivers = false;
        user.CanReports = false;
        user.CanGeofences = false;
        user.CanMaintenance = false;
        user.CanCosts = false;
        user.CanFuel = false;
        user.CanDocuments = false;
        user.CanAccidents = false;
        user.CanUsers = false;
        user.CanSettings = false;
        user.CanSuppliers = false;
        user.CanFleetManagement = false;
        user.CanTours = false;
        user.CanPlayback = false;
        user.CanReportTrips = false;
        user.CanReportFuel = false;
        user.CanReportSpeed = false;
        user.CanReportStops = false;
        user.CanReportMileage = false;
        user.CanReportCosts = false;
        user.CanReportMaintenance = false;
        user.CanReportDaily = false;
        user.CanReportMonthly = false;
        user.CanReportMileagePeriod = false;
        user.CanReportSpeedInfraction = false;
        user.CanReportDrivingBehavior = false;
        user.CanReportMonthlyCosts = false;
        user.CanReportOperatingCost = false;
        user.CanReportCostEvolution = false;
        user.CanReportCostRanking = false;
        user.CanReportRepairFrequency = false;
        user.CanReportMonthlyFuel = false;
        user.CanReportAiFleet = false;
        user.CanReportFuelEstimation = false;
        user.CanReportFuelComparison = false;
        user.AlertAssurance = false;
        user.AlertTaxeCirculation = false;
        user.AlertVisiteTechnique = false;
        user.AlertEntretien = false;
        // Réglages que le chauffeur ne peut plus toucher (/api/reports et /api/users/me lui
        // sont fermés, l'écran Utilisateurs ne les montre pas) : un salarié converti gardait
        // le rapport journalier de TOUTE la flotte par e-mail, et ses heures silencieuses
        // retenaient le push « Nouvelle tournée » d'un départ à l'aube.
        user.DailyReportEmailEnabled = false;
        user.QuietHoursEnabled = false;
    }

    /// <summary>
    /// La fiche <paramref name="driverId"/> de la société peut-elle être reliée au compte
    /// <paramref name="userId"/> (null = compte pas encore créé) ? Refus en clair si elle
    /// n'existe pas dans la société ou porte déjà un AUTRE compte. Appelé par CreateUser
    /// AVANT d'enregistrer le compte : un refus ne laisse pas de compte à moitié créé.
    /// </summary>
    public static async Task<Driver> FindLinkableDriverAsync(
        IGisDbContext context, int companyId, int driverId, int? userId, CancellationToken ct)
    {
        var fiche = await context.Drivers
            .FirstOrDefaultAsync(d => d.Id == driverId && d.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Fiche chauffeur introuvable dans votre société.");
        if (fiche.UserId != null && fiche.UserId != userId)
            throw new ConflictException(
                "Cette fiche chauffeur est déjà reliée à un autre compte : ouvrez ce compte dans Utilisateurs plutôt que d'en créer un second.");
        return fiche;
    }

    /// <summary>
    /// Contrôle, AVANT toute modification, que la fiche <paramref name="driverId"/> peut être
    /// reliée au compte existant <paramref name="userId"/> : mêmes règles que
    /// <see cref="FindLinkableDriverAsync"/>, et le compte ne porte pas déjà une AUTRE fiche.
    /// Sans ce contrôle préalable, le refus tombait dans LinkOrCreateDriverAsync, après que
    /// la modification eut déjà enregistré un rôle.
    /// </summary>
    public static async Task EnsureDriverLinkableToAccountAsync(
        IGisDbContext context, int companyId, int driverId, int userId, CancellationToken ct)
    {
        await FindLinkableDriverAsync(context, companyId, driverId, userId, ct);
        var autreFiche = await context.Drivers
            .AnyAsync(d => d.CompanyId == companyId && d.UserId == userId && d.Id != driverId, ct);
        if (autreFiche)
            throw new ConflictException("Ce compte est déjà relié à une autre fiche chauffeur.");
    }

    /// <summary>
    /// Relie le compte à sa fiche chauffeur. Ordre de recherche : la fiche déjà reliée à ce
    /// compte ; sinon la fiche désignée par <paramref name="driverId"/> (« Créer son compte »
    /// depuis l'écran Chauffeurs, ou « Relier à un compte existant ») ; sinon la fiche de la
    /// société qui porte le même e-mail et n'a pas encore de compte ; sinon une fiche neuve
    /// avec l'identité du compte — seulement pour un compte ACTIF (rend alors null sinon).
    /// Le compte doit déjà être enregistré (Id connu). Idempotent : une fiche déjà reliée
    /// à ce compte est simplement mise à jour (nom, téléphone).
    /// </summary>
    public static async Task<Driver?> LinkOrCreateDriverAsync(IGisDbContext context, User user, int? driverId, CancellationToken ct)
    {
        var existing = await context.Drivers
            .FirstOrDefaultAsync(d => d.CompanyId == user.CompanyId && d.UserId == user.Id, ct);

        // « Créer son compte » depuis une fiche : c'est CETTE fiche (véhicule affecté,
        // permis, tournées déjà planifiées) que le compte doit porter. La retrouver par
        // e-mail échouait dès que la fiche n'en avait pas (champ facultatif) ou que
        // l'admin le corrigeait dans le formulaire : une seconde fiche était créée.
        if (driverId is int ficheId && existing?.Id != ficheId)
        {
            if (existing != null)
                throw new ConflictException(
                    "Ce compte est déjà relié à une autre fiche chauffeur.");
            existing = await FindLinkableDriverAsync(context, user.CompanyId, ficheId, user.Id, ct);
        }

        if (existing == null)
        {
            var email = user.Email.Trim().ToLower();
            existing = await context.Drivers
                .Where(d => d.CompanyId == user.CompanyId && d.UserId == null && d.Email != null)
                .OrderBy(d => d.Id)
                .FirstOrDefaultAsync(d => d.Email!.ToLower() == email, ct);
        }

        if (existing != null)
        {
            // Réactivée seulement quand on la RELIE à un compte actif : une fiche que
            // l'admin a passée « Inactif » ne redevenait « Actif » à chaque modification
            // du compte (téléphone corrigé, compte désactivé…) sans que personne ne le demande.
            var nouvelleLiaison = existing.UserId != user.Id;
            existing.UserId = user.Id;
            existing.FirstName = user.FirstName;
            existing.LastName = user.LastName;
            if (!string.IsNullOrWhiteSpace(user.Phone)) existing.Phone = user.Phone;
            if (string.IsNullOrWhiteSpace(existing.Email)) existing.Email = user.Email;
            if (nouvelleLiaison && user.Status == "active" && existing.Status != "active")
                existing.Status = "active";
            return existing;
        }

        // Compte chauffeur INACTIF sans fiche (fiche supprimée, remise à zéro de la société) :
        // rien n'est créé (relecture du 21/09/2026, R6c). Enregistrer ce compte pour corriger
        // un nom recréait une fiche « Actif » — la fiche supprimée réapparaissait dans l'écran
        // Chauffeurs et les sélecteurs de tournée, pour un compte qui ne se connecte plus. La
        // fiche est créée, ou retrouvée, à la réactivation du compte : même appel.
        if (user.Status != "active") return null;

        var driver = new Driver
        {
            CompanyId = user.CompanyId,
            UserId = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Phone = user.Phone,
            PermitNumber = user.PermitNumber,
            PermitType = user.PermitType,
            PermitExpiry = user.PermitExpiry,
            CIN = user.CIN,
            DateOfBirth = user.DateOfBirth,
            HireDate = user.HireDate,
            Status = "active",
        };
        context.Drivers.Add(driver);
        return driver;
    }

    /// <summary>
    /// Détache la fiche du compte (le compte redevient ordinaire ou disparaît) : la fiche
    /// et son historique restent, seul le lien est coupé.
    /// </summary>
    public static async Task UnlinkDriverAsync(IGisDbContext context, int userId, CancellationToken ct)
    {
        var drivers = await context.Drivers.Where(d => d.UserId == userId).ToListAsync(ct);
        foreach (var d in drivers) d.UserId = null;
    }

    /// <summary>
    /// Ferme l'accès à l'application : compte inactif, sessions révoquées, jetons de
    /// notification éteints. Rien n'est supprimé.
    /// </summary>
    public static async Task RevokeAccessAsync(IGisDbContext context, User user, CancellationToken ct)
    {
        user.Status = "inactive";
        user.UpdatedAt = DateTime.UtcNow;
        await RevokeSessionsAsync(context, user.Id, ct);
        var tokens = await context.UserDeviceTokens
            .Where(t => t.UserId == user.Id && t.IsActive)
            .ToListAsync(ct);
        foreach (var t in tokens) t.IsActive = false;
    }

    /// <summary>
    /// Révoque les sessions (jetons de rafraîchissement) du compte sans toucher à son
    /// statut. Au passage salarié → chauffeur : sa session du site ne doit pas être
    /// prolongée ; à la reconnexion, le site le refuse et l'application lui remet un
    /// jeton « chauffeur ».
    /// </summary>
    public static async Task RevokeSessionsAsync(IGisDbContext context, int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var sessions = await context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions) s.RevokedAt = now;
    }

    /// <summary>
    /// Quota d'utilisateurs de l'abonnement (SubscriptionType.MaxUsers) : les comptes
    /// chauffeurs n'y comptent pas, un compte de gestion de plus doit y trouver sa place.
    /// Partagé par la création ET par le retour d'un chauffeur en compte ordinaire —
    /// sans ce second appel, créer « chauffeur » puis décocher la case donnait un compte
    /// de gestion hors quota, à volonté. <paramref name="exceptUserId"/> : le compte qui
    /// change de type, jamais compté contre lui-même.
    /// </summary>
    public static async Task EnsureStaffSeatAvailableAsync(
        IGisDbContext context, int companyId, int? exceptUserId, CancellationToken ct)
    {
        var company = await context.Societes
            .Include(s => s.SubscriptionType)
            .FirstOrDefaultAsync(s => s.Id == companyId, ct);
        if (company?.SubscriptionType == null) return;

        var currentUsers = await context.Users.CountAsync(
            u => u.CompanyId == companyId && u.AccountType != UserAccountTypes.Driver
                 && (exceptUserId == null || u.Id != exceptUserId), ct);
        if (currentUsers >= company.SubscriptionType.MaxUsers)
            throw new DomainException(
                $"Limite d'utilisateurs atteinte ({company.SubscriptionType.MaxUsers} max pour votre abonnement)");
    }
}
