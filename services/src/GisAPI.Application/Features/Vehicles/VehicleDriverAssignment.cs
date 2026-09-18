using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Affectation chauffeur ↔ véhicule : les deux jambes, <c>vehicles.assigned_driver_id</c>
/// et <c>drivers.assigned_vehicle_id</c>, sont deux colonnes distinctes que les
/// écrans Véhicules et Chauffeurs écrivaient chacun de son côté. Une affectation
/// faite depuis Chauffeurs n'apparaissait donc pas dans la colonne Chauffeur de
/// Véhicules, et l'inverse était vrai aussi (recette GPA du 11/09/2026) : deux
/// écrans, deux vérités.
///
/// <para>Règle : un chauffeur conduit UN véhicule et un véhicule a UN chauffeur.
/// Poser une jambe libère donc l'affectation concurrente de l'autre côté. Ces
/// helpers ne font jamais SaveChanges : l'appelant reste maître de la
/// transaction.</para>
/// </summary>
public static class VehicleDriverAssignment
{
    /// <summary>
    /// À appeler après avoir posé <c>vehicle.AssignedDriverId</c> : recale la
    /// jambe chauffeur (et libère le véhicule que ce chauffeur quittait).
    /// </summary>
    public static async Task SyncDriverSideAsync(IGisDbContext ctx, Vehicle vehicle, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var driverId = vehicle.AssignedDriverId;

        // Chauffeurs encore accrochés à ce véhicule alors qu'il ne leur appartient
        // plus (changement ou retrait de chauffeur) : libérés.
        var stale = await ctx.Drivers
            .Where(d => d.CompanyId == vehicle.CompanyId
                     && d.AssignedVehicleId == vehicle.Id
                     && (driverId == null || d.Id != driverId))
            .ToListAsync(ct);

        foreach (var orphan in stale)
        {
            orphan.AssignedVehicleId = null;
            orphan.UpdatedAt = now;
        }

        if (driverId is not { } id)
            return;

        // vehicles.assigned_driver_id est bien une clé étrangère vers drivers
        // (FK_vehicles_drivers_assigned_driver_id), mais elle ignore la société :
        // la recherche reste bornée à celle du véhicule pour qu'aucun appelant ne
        // puisse déplacer le chauffeur d'une autre société (UpdateVehicle refuse
        // déjà ce cas en amont). Chauffeur déjà d'accord avec le véhicule = rien
        // à faire.
        var driver = await ctx.Drivers
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == vehicle.CompanyId, ct);
        if (driver == null || driver.AssignedVehicleId == vehicle.Id)
            return;

        if (driver.AssignedVehicleId is { } previousVehicleId)
        {
            var previous = await ctx.Vehicles
                .FirstOrDefaultAsync(v => v.Id == previousVehicleId && v.CompanyId == vehicle.CompanyId, ct);
            if (previous != null && previous.AssignedDriverId == driver.Id)
            {
                previous.AssignedDriverId = null;
                previous.UpdatedAt = now;
            }
        }

        driver.AssignedVehicleId = vehicle.Id;
        driver.UpdatedAt = now;
    }

    /// <summary>
    /// À appeler après avoir posé <c>driver.AssignedVehicleId</c> (le chauffeur
    /// doit déjà avoir son id) : recale la jambe véhicule (et libère le chauffeur
    /// que ce véhicule quittait).
    /// </summary>
    public static async Task SyncVehicleSideAsync(IGisDbContext ctx, Driver driver, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var vehicleId = driver.AssignedVehicleId;

        // Véhicules encore accrochés à ce chauffeur alors qu'il n'est plus le leur.
        var stale = await ctx.Vehicles
            .Where(v => v.CompanyId == driver.CompanyId
                     && v.AssignedDriverId == driver.Id
                     && (vehicleId == null || v.Id != vehicleId))
            .ToListAsync(ct);

        foreach (var orphan in stale)
        {
            orphan.AssignedDriverId = null;
            orphan.UpdatedAt = now;
        }

        if (vehicleId is not { } id)
            return;

        var vehicle = await ctx.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == driver.CompanyId, ct);
        if (vehicle == null || vehicle.AssignedDriverId == driver.Id)
            return;

        if (vehicle.AssignedDriverId is { } previousDriverId)
        {
            var previous = await ctx.Drivers
                .FirstOrDefaultAsync(d => d.Id == previousDriverId && d.CompanyId == driver.CompanyId, ct);
            if (previous != null && previous.AssignedVehicleId == vehicle.Id)
            {
                previous.AssignedVehicleId = null;
                previous.UpdatedAt = now;
            }
        }

        vehicle.AssignedDriverId = driver.Id;
        vehicle.UpdatedAt = now;
    }
}
