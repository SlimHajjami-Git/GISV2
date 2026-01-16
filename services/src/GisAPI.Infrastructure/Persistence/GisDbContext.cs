using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Infrastructure.Persistence;

public class GisDbContext : DbContext, IGisDbContext
{
    private readonly ICurrentTenantService? _tenantService;

    public GisDbContext(DbContextOptions<GisDbContext> options) : base(options) { }

    public GisDbContext(DbContextOptions<GisDbContext> options, ICurrentTenantService tenantService) 
        : base(options)
    {
        _tenantService = tenantService;
    }

    // Core
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionType> SubscriptionTypes => Set<SubscriptionType>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Vehicles & Employees
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDocument> VehicleDocuments => Set<VehicleDocument>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<DriverAssignment> DriverAssignments => Set<DriverAssignment>();
    public DbSet<DriverScore> DriverScores => Set<DriverScore>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();

    // GPS & Tracking
    public DbSet<GpsDevice> GpsDevices => Set<GpsDevice>();
    public DbSet<GpsPosition> GpsPositions => Set<GpsPosition>();
    public DbSet<GpsAlert> GpsAlerts => Set<GpsAlert>();
    public DbSet<VehicleStop> VehicleStops => Set<VehicleStop>();
    public DbSet<FuelRecord> FuelRecords => Set<FuelRecord>();

    // Trips & Driving Behavior
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripWaypoint> TripWaypoints => Set<TripWaypoint>();
    public DbSet<DrivingEvent> DrivingEvents => Set<DrivingEvent>();
    public DbSet<DailyStatistics> DailyStatistics => Set<DailyStatistics>();

    // Geofences & POI
    public DbSet<Geofence> Geofences => Set<Geofence>();
    public DbSet<GeofenceVehicle> GeofenceVehicles => Set<GeofenceVehicle>();
    public DbSet<GeofenceEvent> GeofenceEvents => Set<GeofenceEvent>();
    public DbSet<GeofenceGroup> GeofenceGroups => Set<GeofenceGroup>();
    public DbSet<PointOfInterest> PointsOfInterest => Set<PointOfInterest>();
    public DbSet<PoiVisit> PoiVisits => Set<PoiVisit>();

    // Maintenance & Costs
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<MaintenancePart> MaintenanceParts => Set<MaintenancePart>();
    public DbSet<VehicleCost> VehicleCosts => Set<VehicleCost>();

    // Contracts & Reservations
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<Reservation> Reservations => Set<Reservation>();

    // Inventory & Suppliers
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PartInventory> PartInventory => Set<PartInventory>();
    public DbSet<PartTransaction> PartTransactions => Set<PartTransaction>();

    // Reports & Notifications
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from the Infrastructure assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GisDbContext).Assembly);

        // Global query filter for multi-tenancy
        // All entities implementing ITenantEntity will be filtered by CompanyId
        if (_tenantService?.CompanyId != null)
        {
            modelBuilder.Entity<Vehicle>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Employee>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<User>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<GpsDevice>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Geofence>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<MaintenanceRecord>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<VehicleCost>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Trip>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<DrivingEvent>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<DailyStatistics>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<DriverAssignment>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<DriverScore>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<PointOfInterest>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Contract>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Reservation>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Supplier>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<PartInventory>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<PartTransaction>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Report>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<ReportSchedule>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<Notification>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<VehicleStop>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
            modelBuilder.Entity<FuelRecord>().HasQueryFilter(e => e.CompanyId == _tenantService.CompanyId);
        }

        // Configure composite keys
        modelBuilder.Entity<UserVehicle>().HasKey(uv => new { uv.UserId, uv.VehicleId });
        modelBuilder.Entity<GeofenceVehicle>().HasKey(gv => new { gv.GeofenceId, gv.VehicleId });

        // Configure table names (snake_case)
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<Trip>().ToTable("trips");
        modelBuilder.Entity<TripWaypoint>().ToTable("trip_waypoints");
        modelBuilder.Entity<DrivingEvent>().ToTable("driving_events");
        modelBuilder.Entity<DailyStatistics>().ToTable("daily_statistics");
        modelBuilder.Entity<DriverAssignment>().ToTable("driver_assignments");
        modelBuilder.Entity<DriverScore>().ToTable("driver_scores");
        modelBuilder.Entity<UserVehicle>().ToTable("user_vehicles");
        modelBuilder.Entity<PointOfInterest>().ToTable("points_of_interest");
        modelBuilder.Entity<Contract>().ToTable("contracts");
        modelBuilder.Entity<Reservation>().ToTable("reservations");
        modelBuilder.Entity<Supplier>().ToTable("suppliers");
        modelBuilder.Entity<PartInventory>().ToTable("part_inventory");
        modelBuilder.Entity<PartTransaction>().ToTable("part_transactions");
        modelBuilder.Entity<Report>().ToTable("reports");
        modelBuilder.Entity<ReportSchedule>().ToTable("report_schedules");
        modelBuilder.Entity<Notification>().ToTable("notifications");
        modelBuilder.Entity<AuditLog>().ToTable("audit_logs");
        modelBuilder.Entity<VehicleStop>().ToTable("vehicle_stops");
        modelBuilder.Entity<FuelRecord>().ToTable("fuel_records");
        modelBuilder.Entity<SubscriptionType>().ToTable("subscription_types");
        modelBuilder.Entity<Campaign>().ToTable("campaigns");

        // SubscriptionType configuration
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MonthlyPrice).HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().Property(s => s.QuarterlyPrice).HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().Property(s => s.YearlyPrice).HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().HasIndex(s => s.Code).IsUnique();

        // Campaign configuration
        modelBuilder.Entity<Campaign>().Property(c => c.DiscountPercentage).HasPrecision(5, 2);

        // Company column mappings (snake_case)
        modelBuilder.Entity<Company>().Property(c => c.SubscriptionStartedAt).HasColumnName("subscription_started_at");
        modelBuilder.Entity<Company>().Property(c => c.SubscriptionExpiresAt).HasColumnName("subscription_expires_at");
        modelBuilder.Entity<Company>().Property(c => c.BillingCycle).HasColumnName("billing_cycle");
        modelBuilder.Entity<Company>().Property(c => c.SubscriptionStatus).HasColumnName("subscription_status");
        modelBuilder.Entity<Company>().Property(c => c.LastPaymentAt).HasColumnName("last_payment_at");
        modelBuilder.Entity<Company>().Property(c => c.NextPaymentAmount).HasColumnName("next_payment_amount");
        modelBuilder.Entity<Company>().Property(c => c.CampaignId).HasColumnName("campaign_id");
        modelBuilder.Entity<Company>().Property(c => c.IsActive).HasColumnName("is_active");
        modelBuilder.Entity<Company>().Property(c => c.LogoUrl).HasColumnName("logo_url");
        modelBuilder.Entity<Company>().Property(c => c.TaxId).HasColumnName("tax_id");
        modelBuilder.Entity<Company>().Property(c => c.RC).HasColumnName("rc");
        modelBuilder.Entity<Company>().Property(c => c.IF).HasColumnName("if");
        modelBuilder.Entity<Company>().Property(c => c.SubscriptionId).HasColumnName("subscription_id");
        modelBuilder.Entity<Company>().Property(c => c.CreatedAt).HasColumnName("created_at");
        modelBuilder.Entity<Company>().Property(c => c.UpdatedAt).HasColumnName("updated_at");

        // Subscription column mappings (snake_case)
        modelBuilder.Entity<Subscription>().Property(s => s.BillingCycle).HasColumnName("billing_cycle");
        modelBuilder.Entity<Subscription>().Property(s => s.AutoRenew).HasColumnName("auto_renew");
        modelBuilder.Entity<Subscription>().Property(s => s.RenewalReminderDays).HasColumnName("renewal_reminder_days");
        modelBuilder.Entity<Subscription>().Property(s => s.LastRenewalNotificationAt).HasColumnName("last_renewal_notification_at");
        modelBuilder.Entity<Subscription>().Property(s => s.SubscriptionTypeId).HasColumnName("subscription_type_id");
        modelBuilder.Entity<Subscription>().Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Configure unique indexes
        modelBuilder.Entity<DailyStatistics>()
            .HasIndex(s => new { s.VehicleId, s.Date })
            .IsUnique();

        modelBuilder.Entity<DriverScore>()
            .HasIndex(s => new { s.DriverId, s.Date })
            .IsUnique();

        // Configure JSONB columns for PostgreSQL
        modelBuilder.Entity<Notification>()
            .Property(n => n.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<DrivingEvent>()
            .Property(e => e.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.OldValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.NewValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<PointOfInterest>()
            .Property(p => p.Hours)
            .HasColumnType("jsonb");

        // Configure array columns
        modelBuilder.Entity<Report>()
            .Property(r => r.VehicleIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<Report>()
            .Property(r => r.DriverIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(r => r.EmailRecipients)
            .HasColumnType("text[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(r => r.VehicleIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(r => r.DriverIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<PartInventory>()
            .Property(p => p.CompatibleVehicles)
            .HasColumnType("text[]");

        // Configure decimal precision
        modelBuilder.Entity<Trip>().Property(t => t.DistanceKm).HasPrecision(10, 2);
        modelBuilder.Entity<Trip>().Property(t => t.FuelConsumedLiters).HasPrecision(10, 2);
        modelBuilder.Entity<Trip>().Property(t => t.AverageSpeedKph).HasPrecision(6, 2);
        modelBuilder.Entity<Trip>().Property(t => t.MaxSpeedKph).HasPrecision(6, 2);

        modelBuilder.Entity<DailyStatistics>().Property(s => s.DistanceKm).HasPrecision(10, 2);
        modelBuilder.Entity<DailyStatistics>().Property(s => s.AverageSpeedKph).HasPrecision(6, 2);
        modelBuilder.Entity<DailyStatistics>().Property(s => s.MaxSpeedKph).HasPrecision(6, 2);
        modelBuilder.Entity<DailyStatistics>().Property(s => s.FuelConsumedLiters).HasPrecision(10, 2);
        modelBuilder.Entity<DailyStatistics>().Property(s => s.FuelEfficiencyKmPerLiter).HasPrecision(6, 2);
        modelBuilder.Entity<DailyStatistics>().Property(s => s.FuelCost).HasPrecision(10, 2);

        modelBuilder.Entity<DriverScore>().Property(s => s.DistanceKm).HasPrecision(10, 2);

        modelBuilder.Entity<Contract>().Property(c => c.TotalValue).HasPrecision(12, 2);
        modelBuilder.Entity<Contract>().Property(c => c.MonthlyPayment).HasPrecision(10, 2);
        modelBuilder.Entity<Contract>().Property(c => c.ExcessKmRate).HasPrecision(6, 2);
        modelBuilder.Entity<Contract>().Property(c => c.ResidualValue).HasPrecision(12, 2);
        modelBuilder.Entity<Contract>().Property(c => c.Deductible).HasPrecision(10, 2);

        modelBuilder.Entity<PartInventory>().Property(p => p.UnitCost).HasPrecision(10, 2);
        modelBuilder.Entity<PartInventory>().Property(p => p.SellingPrice).HasPrecision(10, 2);

        modelBuilder.Entity<PartTransaction>().Property(t => t.UnitCost).HasPrecision(10, 2);
        modelBuilder.Entity<PartTransaction>().Property(t => t.TotalCost).HasPrecision(10, 2);

        modelBuilder.Entity<Supplier>().Property(s => s.DiscountPercent).HasPrecision(5, 2);

        // FuelRecord decimal precision
        modelBuilder.Entity<FuelRecord>().Property(f => f.FuelLiters).HasPrecision(10, 2);
        modelBuilder.Entity<FuelRecord>().Property(f => f.TankCapacityLiters).HasPrecision(10, 2);
        modelBuilder.Entity<FuelRecord>().Property(f => f.ConsumptionRateLPer100Km).HasPrecision(6, 2);
        modelBuilder.Entity<FuelRecord>().Property(f => f.AverageConsumptionLPer100Km).HasPrecision(6, 2);
        modelBuilder.Entity<FuelRecord>().Property(f => f.RefuelAmount).HasPrecision(10, 2);
        modelBuilder.Entity<FuelRecord>().Property(f => f.RefuelCost).HasPrecision(10, 2);

        // GpsPosition decimal precision
        modelBuilder.Entity<GpsPosition>().Property(p => p.FuelRateLPer100Km).HasPrecision(6, 2);

        // Performance indexes for GPS positions (high volume table)
        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => new { p.DeviceId, p.RecordedAt })
            .HasDatabaseName("ix_gps_positions_device_time");

        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => p.RecordedAt)
            .HasDatabaseName("ix_gps_positions_time");

        // Performance indexes for vehicle stops
        modelBuilder.Entity<VehicleStop>()
            .HasIndex(s => new { s.VehicleId, s.StartTime })
            .HasDatabaseName("ix_vehicle_stops_vehicle_time");

        // Performance indexes for fuel records
        modelBuilder.Entity<FuelRecord>()
            .HasIndex(f => new { f.VehicleId, f.RecordedAt })
            .HasDatabaseName("ix_fuel_records_vehicle_time");
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

        // Auto-set CompanyId for new tenant entities
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
