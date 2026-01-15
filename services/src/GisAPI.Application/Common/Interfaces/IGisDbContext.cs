using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Interfaces;

public interface IGisDbContext
{
    DbSet<Subscription> Subscriptions { get; }
    DbSet<SubscriptionType> SubscriptionTypes { get; }
    DbSet<Campaign> Campaigns { get; }
    DbSet<Company> Companies { get; }
    DbSet<User> Users { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<VehicleDocument> VehicleDocuments { get; }
    DbSet<Employee> Employees { get; }
    DbSet<GpsDevice> GpsDevices { get; }
    DbSet<GpsPosition> GpsPositions { get; }
    DbSet<GpsAlert> GpsAlerts { get; }
    DbSet<Geofence> Geofences { get; }
    DbSet<GeofenceVehicle> GeofenceVehicles { get; }
    DbSet<GeofenceEvent> GeofenceEvents { get; }
    DbSet<MaintenanceRecord> MaintenanceRecords { get; }
    DbSet<MaintenancePart> MaintenanceParts { get; }
    DbSet<VehicleCost> VehicleCosts { get; }
    DbSet<VehicleStop> VehicleStops { get; }
    DbSet<FuelRecord> FuelRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
