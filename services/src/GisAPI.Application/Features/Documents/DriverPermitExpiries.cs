using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents;

/// <summary>
/// Échéances des permis de conducteur (table drivers), définition unique du
/// périmètre et de la ligne partagée par la liste des échéances, les compteurs
/// et les alertes. Constat (DEF-033) : les alertes ne lisaient que les
/// véhicules, si bien qu'un permis expiré figurait dans la liste et les
/// compteurs mais jamais dans les alertes.
/// </summary>
public static class DriverPermitExpiries
{
    public const string Type = "driver_permit";

    /// <summary>
    /// Chauffeurs de la société dont le permis a une échéance. Un utilisateur
    /// restreint (<paramref name="accessibleVehicleIds"/> non nul) ne voit que
    /// les chauffeurs affectés aux véhicules qu'il a le droit de voir.
    /// </summary>
    public static Task<List<Driver>> LoadAsync(
        IGisDbContext context, int companyId, List<int>? accessibleVehicleIds, CancellationToken ct)
    {
        var query = context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.PermitExpiry != null);

        if (accessibleVehicleIds is not null)
            query = query.Where(d => d.AssignedVehicleId != null
                                     && accessibleVehicleIds.Contains(d.AssignedVehicleId.Value));

        return query.ToListAsync(ct);
    }

    /// <summary>
    /// Ligne d'échéance d'un permis : le chauffeur tient lieu de nom, le type de
    /// permis de plaque. Le véhicule vient de driver.AssignedVehicleId, la
    /// source de vérité (vehicles.AssignedDriverId n'est pas toujours tenu à jour
    /// par les formulaires qui n'écrivent que le côté chauffeur).
    /// </summary>
    public static VehicleExpiryDto ToDto(
        Driver driver, IEnumerable<Vehicle> vehicles, string status, int daysUntil)
    {
        var assignedVehicle = driver.AssignedVehicleId.HasValue
            ? vehicles.FirstOrDefault(v => v.Id == driver.AssignedVehicleId.Value)
            : null;

        return new VehicleExpiryDto(
            assignedVehicle?.Id ?? 0,
            driver.FullName,
            driver.PermitType != null ? $"Permis {driver.PermitType}" : "Permis",
            Type,
            ExpiryCalendar.Day(driver.PermitExpiry),
            status,
            daysUntil,
            null,
            null,
            driver.PermitNumber);
    }
}
