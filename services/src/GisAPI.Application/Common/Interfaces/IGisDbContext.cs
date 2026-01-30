using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Interfaces;

public interface IGisDbContext
{
    DbSet<SubscriptionType> SubscriptionTypes { get; }
    DbSet<Societe> Societes { get; }
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<Vehicle> Vehicles { get; }
    DbSet<VehicleDocument> VehicleDocuments { get; }
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
    DbSet<VehicleAssignment> VehicleAssignments { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<SupplierService> SupplierServices { get; }
    DbSet<AccidentClaim> AccidentClaims { get; }
    DbSet<AccidentClaimThirdParty> AccidentClaimThirdParties { get; }
    DbSet<AccidentClaimDocument> AccidentClaimDocuments { get; }
    DbSet<MaintenanceTemplate> MaintenanceTemplates { get; }
    DbSet<VehicleMaintenanceSchedule> VehicleMaintenanceSchedules { get; }
    DbSet<MaintenanceLog> MaintenanceLogs { get; }
    
    // Fleet Management
    DbSet<Department> Departments { get; }
    DbSet<FuelType> FuelTypes { get; }
    DbSet<FuelPricing> FuelPricings { get; }
    DbSet<SpeedLimitAlert> SpeedLimitAlerts { get; }
    
    // Brands & Models
    DbSet<Brand> Brands { get; }
    DbSet<VehicleModel> VehicleModels { get; }
    DbSet<PartCategory> PartCategories { get; }
    DbSet<VehiclePart> VehicleParts { get; }
    DbSet<PartPricing> PartPricings { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}



