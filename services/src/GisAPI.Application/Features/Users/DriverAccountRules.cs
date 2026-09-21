using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
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
    }

    /// <summary>
    /// Relie le compte à sa fiche chauffeur : la fiche de la société qui porte le même
    /// e-mail et n'a pas encore de compte, sinon une fiche neuve avec l'identité du compte.
    /// Le compte doit déjà être enregistré (Id connu). Idempotent : une fiche déjà reliée
    /// à ce compte est simplement mise à jour (nom, téléphone).
    /// </summary>
    public static async Task<Driver> LinkOrCreateDriverAsync(IGisDbContext context, User user, CancellationToken ct)
    {
        var existing = await context.Drivers
            .FirstOrDefaultAsync(d => d.CompanyId == user.CompanyId && d.UserId == user.Id, ct);
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
            existing.UserId = user.Id;
            existing.FirstName = user.FirstName;
            existing.LastName = user.LastName;
            if (!string.IsNullOrWhiteSpace(user.Phone)) existing.Phone = user.Phone;
            if (existing.Status != "active") existing.Status = "active";
            return existing;
        }

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
        var now = DateTime.UtcNow;
        var sessions = await context.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions) s.RevokedAt = now;
        var tokens = await context.UserDeviceTokens
            .Where(t => t.UserId == user.Id && t.IsActive)
            .ToListAsync(ct);
        foreach (var t in tokens) t.IsActive = false;
    }
}
