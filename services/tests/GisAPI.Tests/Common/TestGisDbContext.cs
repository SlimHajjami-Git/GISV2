using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Tests.Common;

// Alias for tests - Company is same as Societe
public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
}

public class TestGisDbContext : DbContext, IGisDbContext
{
    private readonly ICurrentTenantService? _tenantService;

    public TestGisDbContext(DbContextOptions<TestGisDbContext> options) : base(options) { }

    public TestGisDbContext(DbContextOptions<TestGisDbContext> options, ICurrentTenantService tenantService) 
        : base(options)
    {
        _tenantService = tenantService;
    }

    public DbSet<SubscriptionType> SubscriptionTypes => Set<SubscriptionType>();
    public DbSet<SubscriptionOrder> SubscriptionOrders => Set<SubscriptionOrder>();
    public DbSet<Societe> Societes => Set<Societe>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDocument> VehicleDocuments => Set<VehicleDocument>();
    public DbSet<GpsDevice> GpsDevices => Set<GpsDevice>();
    public DbSet<GpsPosition> GpsPositions => Set<GpsPosition>();
    public DbSet<GpsAlert> GpsAlerts => Set<GpsAlert>();
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<GeofenceVehicle> GeofenceVehicles => Set<GeofenceVehicle>();
    public DbSet<GeofenceEvent> GeofenceEvents => Set<GeofenceEvent>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<MaintenancePart> MaintenanceParts => Set<MaintenancePart>();
    public DbSet<VehicleCost> VehicleCosts => Set<VehicleCost>();
    public DbSet<VehicleStop> VehicleStops => Set<VehicleStop>();
    public DbSet<FuelRecord> FuelRecords => Set<FuelRecord>();
    public DbSet<VehicleAssignment> VehicleAssignments => Set<VehicleAssignment>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierService> SupplierServices => Set<SupplierService>();
    // Calypso 7 — AccidentClaim merged into AccidentEvent.
    public DbSet<AccidentEvent> AccidentEvents => Set<AccidentEvent>();
    public DbSet<AccidentEventDocument> AccidentEventDocuments => Set<AccidentEventDocument>();
    public DbSet<AccidentEventThirdParty> AccidentEventThirdParties => Set<AccidentEventThirdParty>();
    public DbSet<MaintenanceTemplate> MaintenanceTemplates => Set<MaintenanceTemplate>();
    public DbSet<VehicleMaintenanceSchedule> VehicleMaintenanceSchedules => Set<VehicleMaintenanceSchedule>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    public DbSet<MaintenanceTemplatePart> MaintenanceTemplateParts => Set<MaintenanceTemplatePart>();
    public DbSet<MaintenanceNotification> MaintenanceNotifications => Set<MaintenanceNotification>();
    public DbSet<MaintenanceAlertSettings> MaintenanceAlertSettings => Set<MaintenanceAlertSettings>();
    
    // Fleet Management
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<FuelType> FuelTypes => Set<FuelType>();
    public DbSet<FuelPricing> FuelPricings => Set<FuelPricing>();
    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();
    public DbSet<SpeedLimitAlert> SpeedLimitAlerts => Set<SpeedLimitAlert>();
    public DbSet<Driver> Drivers => Set<Driver>();
    
    // Brands & Models
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<VehicleModel> VehicleModels => Set<VehicleModel>();
    public DbSet<PartCategory> PartCategories => Set<PartCategory>();
    public DbSet<VehiclePart> VehicleParts => Set<VehiclePart>();
    public DbSet<PartPricing> PartPricings => Set<PartPricing>();

    // Repairs
    public DbSet<Repair> Repairs => Set<Repair>();
    public DbSet<RepairPart> RepairParts => Set<RepairPart>();

    // Notifications
    public DbSet<Notification> Notifications => Set<Notification>();

    // Tours
    public DbSet<Tour> Tours => Set<Tour>();
    public DbSet<TourWaypoint> TourWaypoints => Set<TourWaypoint>();
    public DbSet<TourPause> TourPauses => Set<TourPause>();

    // Trips
    public DbSet<Trip> Trips => Set<Trip>();

    // Chat
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AiChatMessage> AiChatMessages => Set<AiChatMessage>();

    // Auth
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Device Commands
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();

    // Device Events
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();

    // User Device Tokens
    public DbSet<UserDeviceToken> UserDeviceTokens => Set<UserDeviceToken>();

    // Reservations / Emprunts
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Contract> Contracts => Set<Contract>();

    // Alert Emails
    public DbSet<AlertEmail> AlertEmails => Set<AlertEmail>();

    // Invoice scan quota
    public DbSet<InvoiceScanLog> InvoiceScanLogs => Set<InvoiceScanLog>();

    // Towing
    public DbSet<TowEvent> TowEvents => Set<TowEvent>();

    // Argus tunisien (global reference data — no tenant filter)
    public DbSet<CarMarketModel> CarMarketModels => Set<CarMarketModel>();

    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // === User: computed properties + legacy cached fields ===
        modelBuilder.Entity<User>()
            .Ignore(u => u.FullName)
            .Ignore(u => u.IsCompanyAdmin)
            .Ignore(u => u.IsSystemAdmin)
            .Ignore(u => u.IsAnyAdmin)
            .Ignore(u => u.Name)
            .Ignore(u => u.Roles)
            .Ignore(u => u.Permissions)
            .Ignore(u => u.AssignedVehicleIds)
            .Ignore(u => u.UserType)
            .Ignore(u => u.IsDriver)
            .Ignore(u => u.UserVehicles);
        modelBuilder.Entity<User>().Ignore("_rolesCache");
        modelBuilder.Entity<User>().Ignore("_permissionsCache");
        modelBuilder.Entity<User>().Ignore("_assignedVehicleIdsCache");
        modelBuilder.Entity<User>().Ignore("_userTypeCache");

        // === UserVehicle: navigation properties ===
        modelBuilder.Entity<UserVehicle>()
            .Ignore(uv => uv.AssignedBy)
            .Ignore(uv => uv.User)
            .Ignore(uv => uv.Vehicle);

        // === Role: computed property ===
        modelBuilder.Entity<Role>()
            .Ignore(r => r.Permissions)
            .Ignore(r => r.IsSystemAdmin);

        // === UserSettings: owned JSON types ===
        modelBuilder.Entity<UserSettings>()
            .Ignore(us => us.Notifications)
            .Ignore(us => us.Display);

        // === Societe: complex types + navigations that cascade to problematic entities ===
        modelBuilder.Entity<Societe>()
            .Ignore(c => c.Settings)
            .Ignore(c => c.PointsOfInterest)
            .Ignore(c => c.Reports)
            .Ignore(c => c.ReportSchedules);

        // === Composite keys (entities without Id) ===
        modelBuilder.Entity<GeofenceVehicle>().HasKey(gv => new { gv.GeofenceId, gv.VehicleId });

        // === Geofence: array/complex types + navigation to non-DbSet ===
        modelBuilder.Entity<Geofence>()
            .Ignore(g => g.Coordinates)
            .Ignore(g => g.ActiveDays)
            .Ignore(g => g.Group);

        // === GPS: JSONB metadata ===
        modelBuilder.Entity<GpsPosition>().Ignore(p => p.Metadata);
        modelBuilder.Entity<Notification>().Ignore(n => n.Metadata);

        // === AuditLog: Dictionary<string,object> JSON columns (jsonb in prod) ===
        modelBuilder.Entity<AuditLog>().Ignore(a => a.OldValues).Ignore(a => a.NewValues);

        // === SubscriptionType: JSONB ===
        modelBuilder.Entity<SubscriptionType>().Ignore(s => s.AccessRights);

        // === Maintenance: arrays and complex types ===
        modelBuilder.Entity<MaintenanceTemplate>().Ignore(mt => mt.AppliesToVehicleTypes);
        modelBuilder.Entity<MaintenanceLog>()
            .Ignore(ml => ml.PartsReplaced)
            .Ignore(ml => ml.Photos);
        modelBuilder.Entity<MaintenanceNotification>().Ignore(mn => mn.SentChannels);
        modelBuilder.Entity<MaintenanceAlertSettings>()
            .Ignore(ma => ma.AdditionalEmails)
            .Ignore(ma => ma.AdditionalPhones);

        // === DeviceEvent: Dictionary<string, object> ===
        modelBuilder.Entity<DeviceEvent>().Ignore(de => de.Details);

        // === Reservation: FK relationships ===
        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.Vehicle).WithMany().HasForeignKey(r => r.VehicleId).IsRequired(false);
        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.RequestedByUser).WithMany().HasForeignKey(r => r.RequestedByUserId).IsRequired(false);
        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.AssignedDriver).WithMany().HasForeignKey(r => r.AssignedDriverId).IsRequired(false);
        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.ApprovedByUser).WithMany().HasForeignKey(r => r.ApprovedByUserId).IsRequired(false);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    break;
            }
        }

        if (_tenantService?.CompanyId != null)
        {
            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added && entry.Entity.CompanyId == 0)
                {
                    entry.Entity.CompanyId = _tenantService.CompanyId.Value;
                }
            }
        }

        return await base.SaveChangesAsync(ct);
    }
}


