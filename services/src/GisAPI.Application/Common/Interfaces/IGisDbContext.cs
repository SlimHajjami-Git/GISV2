using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Interfaces;

public interface IGisDbContext
{
    DbSet<SubscriptionType> SubscriptionTypes { get; }
    DbSet<Societe> Societes { get; }
    DbSet<Role> Roles { get; }
    DbSet<User> Users { get; }
    DbSet<Driver> Drivers { get; }
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
    DbSet<UserVehicle> UserVehicles { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<SupplierService> SupplierServices { get; }
    // Calypso 7 — AccidentClaim was unified into AccidentEvent. Documents
    // and third parties are now child tables of accident_events.
    DbSet<AccidentEvent> AccidentEvents { get; }
    DbSet<AccidentEventDocument> AccidentEventDocuments { get; }
    DbSet<AccidentEventThirdParty> AccidentEventThirdParties { get; }
    DbSet<MaintenanceTemplate> MaintenanceTemplates { get; }
    DbSet<VehicleMaintenanceSchedule> VehicleMaintenanceSchedules { get; }
    DbSet<MaintenanceLog> MaintenanceLogs { get; }
    DbSet<MaintenanceTemplatePart> MaintenanceTemplateParts { get; }
    DbSet<MaintenanceNotification> MaintenanceNotifications { get; }
    DbSet<MaintenanceAlertSettings> MaintenanceAlertSettings { get; }
    
    // Fleet Management
    DbSet<Department> Departments { get; }
    DbSet<FuelType> FuelTypes { get; }
    DbSet<FuelPricing> FuelPricings { get; }
    DbSet<FuelEntry> FuelEntries { get; }
    DbSet<SpeedLimitAlert> SpeedLimitAlerts { get; }
    
    // Brands & Models
    DbSet<Brand> Brands { get; }
    DbSet<VehicleModel> VehicleModels { get; }
    DbSet<PartCategory> PartCategories { get; }
    DbSet<VehiclePart> VehicleParts { get; }
    DbSet<PartPricing> PartPricings { get; }
    
    // Repairs
    DbSet<Repair> Repairs { get; }
    DbSet<RepairPart> RepairParts { get; }
    
    // Notifications
    DbSet<Notification> Notifications { get; }
    DbSet<UserDeviceToken> UserDeviceTokens { get; }

    // Tours
    DbSet<Tour> Tours { get; }
    DbSet<TourWaypoint> TourWaypoints { get; }
    DbSet<TourPause> TourPauses { get; }

    // Trips
    DbSet<Trip> Trips { get; }

    // Chat
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<AiChatMessage> AiChatMessages { get; }

    // Device Events
    DbSet<DeviceEvent> DeviceEvents { get; }

    // Device Commands (GO / STOP / CONFN config sent over TCP via Rust)
    DbSet<DeviceCommand> DeviceCommands { get; }

    // Auth
    DbSet<RefreshToken> RefreshTokens { get; }

    // Reservations / Emprunts
    DbSet<Reservation> Reservations { get; }
    DbSet<Contract> Contracts { get; }

    // Alert Emails
    DbSet<AlertEmail> AlertEmails { get; }

    /// <summary>
    /// Underlying database facade — exposed so MediatR handlers can run
    /// raw SQL (<c>Database.SqlQueryRaw&lt;T&gt;</c>) for queries the
    /// LINQ provider can't express efficiently, e.g. <c>DISTINCT ON</c>
    /// in Postgres. Same surface as <c>DbContext.Database</c>; do not
    /// abuse it — prefer LINQ when it's expressive enough.
    /// </summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}



