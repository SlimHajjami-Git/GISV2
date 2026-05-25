using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Infrastructure.Persistence;

public class GisDbContext : DbContext, IGisDbContext
{
    private readonly ICurrentTenantService? _tenantService;
    private readonly IPublisher? _publisher;

    public GisDbContext(DbContextOptions<GisDbContext> options) : base(options) { }

    public GisDbContext(DbContextOptions<GisDbContext> options, ICurrentTenantService tenantService) 
        : base(options)
    {
        _tenantService = tenantService;
    }

    public GisDbContext(DbContextOptions<GisDbContext> options, ICurrentTenantService tenantService, IPublisher publisher) 
        : base(options)
    {
        _tenantService = tenantService;
        _publisher = publisher;
    }

    // Core
    public DbSet<SubscriptionType> SubscriptionTypes => Set<SubscriptionType>();
    public DbSet<Societe> Societes => Set<Societe>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Vehicles
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDocument> VehicleDocuments => Set<VehicleDocument>();
    public DbSet<VehicleAssignment> VehicleAssignments => Set<VehicleAssignment>();
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
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();

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
    public DbSet<SupplierService> SupplierServices => Set<SupplierService>();
    public DbSet<PartInventory> PartInventory => Set<PartInventory>();
    public DbSet<PartTransaction> PartTransactions => Set<PartTransaction>();

    // Reports & Notifications
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserDeviceToken> UserDeviceTokens => Set<UserDeviceToken>();

    // Calypso 7 — AccidentClaim merged into AccidentEvent. Documents and
    // third parties are now child tables of accident_events.
    public DbSet<AccidentEvent> AccidentEvents => Set<AccidentEvent>();
    public DbSet<AccidentEventDocument> AccidentEventDocuments => Set<AccidentEventDocument>();
    public DbSet<AccidentEventThirdParty> AccidentEventThirdParties => Set<AccidentEventThirdParty>();

    // Standalone tow detection (engine-off + speed + displacement), independent
    // of accidents — surfaced on the dedicated /remorquages page.
    public DbSet<TowEvent> TowEvents => Set<TowEvent>();

    // Maintenance Templates
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
    
    // Brands & Models
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<VehicleModel> VehicleModels => Set<VehicleModel>();
    public DbSet<PartCategory> PartCategories => Set<PartCategory>();
    public DbSet<VehiclePart> VehicleParts => Set<VehiclePart>();
    public DbSet<PartPricing> PartPricings => Set<PartPricing>();
    
    // Repairs
    public DbSet<Repair> Repairs => Set<Repair>();
    public DbSet<RepairPart> RepairParts => Set<RepairPart>();

    // Tours
    public DbSet<Tour> Tours => Set<Tour>();
    public DbSet<TourWaypoint> TourWaypoints => Set<TourWaypoint>();
    public DbSet<TourPause> TourPauses => Set<TourPause>();

    // Chat
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AiChatMessage> AiChatMessages => Set<AiChatMessage>();

    // Alert Emails
    public DbSet<AlertEmail> AlertEmails => Set<AlertEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from the Infrastructure assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GisDbContext).Assembly);

        // Global query filter for multi-tenancy
        // All entities implementing ITenantEntity will be filtered by CompanyId
        // Bypass conditions (no filtering): migrations (_tenantService null), unauthenticated (CompanyId null), system admins
        modelBuilder.Entity<Vehicle>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<User>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<GpsDevice>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Geofence>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<MaintenanceRecord>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<VehicleCost>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Trip>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<DrivingEvent>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<DeviceEvent>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<DailyStatistics>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<DriverAssignment>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<DriverScore>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<PointOfInterest>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Contract>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Reservation>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Supplier>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<PartInventory>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<PartTransaction>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Report>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<ReportSchedule>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Notification>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<VehicleStop>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<FuelRecord>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<AccidentEvent>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<TowEvent>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<MaintenanceTemplate>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<GpsAlert>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<VehicleMaintenanceSchedule>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Driver>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<Tour>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<ChatMessage>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<AiChatMessage>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);
        modelBuilder.Entity<AlertEmail>().HasQueryFilter(e => _tenantService == null || _tenantService.CompanyId == null || _tenantService.IsSystemAdmin || e.CompanyId == _tenantService.CompanyId);

        // Configure composite keys
        modelBuilder.Entity<GeofenceVehicle>().HasKey(gv => new { gv.GeofenceId, gv.VehicleId });

        // Configure table names (snake_case)
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<Trip>().ToTable("trips");
        modelBuilder.Entity<TripWaypoint>().ToTable("trip_waypoints");
        modelBuilder.Entity<DrivingEvent>().ToTable("driving_events");
        // DeviceEvent config in DeviceEventConfiguration.cs
        modelBuilder.Entity<DailyStatistics>().ToTable("daily_statistics");
        modelBuilder.Entity<Driver>().ToTable("drivers");
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
        modelBuilder.Entity<FuelType>().ToTable("fuel_types");
        modelBuilder.Entity<FuelPricing>().ToTable("fuel_pricing");
        modelBuilder.Entity<FuelEntry>().ToTable("fuel_entries");
        modelBuilder.Entity<VehicleAssignment>().ToTable("vehicle_user_assignments");
        modelBuilder.Entity<SubscriptionType>().ToTable("subscription_types");
        modelBuilder.Entity<Societe>().ToTable("societes");
        modelBuilder.Entity<Role>().ToTable("roles");
        modelBuilder.Entity<Tour>().ToTable("tours");
        modelBuilder.Entity<TourWaypoint>().ToTable("tour_waypoints");
        modelBuilder.Entity<TourWaypoint>().Property(w => w.GeofenceId).HasColumnName("geofence_id");
        modelBuilder.Entity<TourWaypoint>().Property(w => w.EstimatedLegMinutes).HasColumnName("estimated_leg_minutes");
        modelBuilder.Entity<TourWaypoint>().Property(w => w.DeadlineMarginMinutes).HasColumnName("deadline_margin_minutes");
        modelBuilder.Entity<TourWaypoint>().Property(w => w.WaypointStatus).HasColumnName("waypoint_status");
        modelBuilder.Entity<TourPause>().ToTable("tour_pauses");
        modelBuilder.Entity<ChatMessage>().ToTable("chat_messages");
        modelBuilder.Entity<AiChatMessage>().ToTable("ai_chat_messages");
        modelBuilder.Entity<AlertEmail>().ToTable("alert_emails");

        // UserDeviceToken — FCM push notification targeting (one row per (user, device))
        modelBuilder.Entity<UserDeviceToken>().ToTable("user_device_tokens");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.Id).HasColumnName("id");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.UserId).HasColumnName("user_id");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.Token).HasColumnName("token");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.Platform).HasColumnName("platform").HasMaxLength(20);
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.IsActive).HasColumnName("is_active");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.RegisteredAt).HasColumnName("registered_at");
        modelBuilder.Entity<UserDeviceToken>().Property(t => t.LastUsedAt).HasColumnName("last_used_at");
        modelBuilder.Entity<UserDeviceToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<UserDeviceToken>().HasIndex(t => t.Token).IsUnique();
        modelBuilder.Entity<UserDeviceToken>().HasIndex(t => new { t.UserId, t.IsActive });

        // SubscriptionType configuration - all column mappings for PostgreSQL snake_case
        modelBuilder.Entity<SubscriptionType>().Property(s => s.Id).HasColumnName("id");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.Name).HasColumnName("name");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.Code).HasColumnName("code");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.Description).HasColumnName("description");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.TargetCompanyType).HasColumnName("target_company_type");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MonthlyPrice).HasColumnName("monthly_price").HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().Property(s => s.QuarterlyPrice).HasColumnName("quarterly_price").HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().Property(s => s.YearlyPrice).HasColumnName("yearly_price").HasPrecision(10, 2);
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MonthlyDurationDays).HasColumnName("monthly_duration_days");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.QuarterlyDurationDays).HasColumnName("quarterly_duration_days");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.YearlyDurationDays).HasColumnName("yearly_duration_days");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MaxVehicles).HasColumnName("max_vehicles");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MaxUsers).HasColumnName("max_users");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MaxGpsDevices).HasColumnName("max_gps_devices");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.MaxGeofences).HasColumnName("max_geofences");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.GpsTracking).HasColumnName("gps_tracking");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.GpsInstallation).HasColumnName("gps_installation");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ApiAccess).HasColumnName("api_access");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.AdvancedReports).HasColumnName("advanced_reports");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.RealTimeAlerts).HasColumnName("real_time_alerts");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.HistoryPlayback).HasColumnName("history_playback");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.FuelAnalysis).HasColumnName("fuel_analysis");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.DrivingBehavior).HasColumnName("driving_behavior");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.HistoryRetentionDays).HasColumnName("history_retention_days");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.SortOrder).HasColumnName("sort_order");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.IsActive).HasColumnName("is_active");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.AccessRights).HasColumnType("jsonb").HasColumnName("access_rights");
        // Module permissions
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleDashboard).HasColumnName("module_dashboard");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleMonitoring).HasColumnName("module_monitoring");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleVehicles).HasColumnName("module_vehicles");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleEmployees).HasColumnName("module_employees");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleGeofences).HasColumnName("module_geofences");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleMaintenance).HasColumnName("module_maintenance");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleCosts).HasColumnName("module_costs");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleReports).HasColumnName("module_reports");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleSettings).HasColumnName("module_settings");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleUsers).HasColumnName("module_users");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleSuppliers).HasColumnName("module_suppliers");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleDocuments).HasColumnName("module_documents");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleAccidents).HasColumnName("module_accidents");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ModuleFleetManagement).HasColumnName("module_fleet_management");
        // Report permissions
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportTrips).HasColumnName("report_trips");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportFuel).HasColumnName("report_fuel");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportSpeed).HasColumnName("report_speed");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportStops).HasColumnName("report_stops");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportMileage).HasColumnName("report_mileage");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportCosts).HasColumnName("report_costs");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportMaintenance).HasColumnName("report_maintenance");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportDaily).HasColumnName("report_daily");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportMonthly).HasColumnName("report_monthly");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportMileagePeriod).HasColumnName("report_mileage_period");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportSpeedInfraction).HasColumnName("report_speed_infraction");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.ReportDrivingBehavior).HasColumnName("report_driving_behavior");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.CreatedAt).HasColumnName("created_at");
        modelBuilder.Entity<SubscriptionType>().Property(s => s.UpdatedAt).HasColumnName("updated_at");
        modelBuilder.Entity<SubscriptionType>().HasIndex(s => s.Code).IsUnique();
        
        // VehicleAssignment configuration
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.VehicleId).HasColumnName("vehicle_id");
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.UserId).HasColumnName("user_id");
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.AssignedAt).HasColumnName("assigned_at");
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.UnassignedAt).HasColumnName("unassigned_at");
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.IsActive).HasColumnName("is_active");
        modelBuilder.Entity<VehicleAssignment>().Property(a => a.AssignedBy).HasColumnName("assigned_by");
        modelBuilder.Entity<VehicleAssignment>().HasOne(a => a.Vehicle).WithMany().HasForeignKey(a => a.VehicleId);
        modelBuilder.Entity<VehicleAssignment>().HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId);

        // User permission column mappings (snake_case)
        modelBuilder.Entity<User>().Property(u => u.AccessLevel).HasColumnName("access_level");
        modelBuilder.Entity<User>().Property(u => u.CanMonitoring).HasColumnName("can_monitoring");
        modelBuilder.Entity<User>().Property(u => u.CanVehicles).HasColumnName("can_vehicles");
        modelBuilder.Entity<User>().Property(u => u.CanDrivers).HasColumnName("can_drivers");
        modelBuilder.Entity<User>().Property(u => u.CanReports).HasColumnName("can_reports");
        modelBuilder.Entity<User>().Property(u => u.CanGeofences).HasColumnName("can_geofences");
        modelBuilder.Entity<User>().Property(u => u.CanMaintenance).HasColumnName("can_maintenance");
        modelBuilder.Entity<User>().Property(u => u.CanCosts).HasColumnName("can_costs");
        modelBuilder.Entity<User>().Property(u => u.CanDocuments).HasColumnName("can_documents");
        modelBuilder.Entity<User>().Property(u => u.CanAccidents).HasColumnName("can_accidents");
        modelBuilder.Entity<User>().Property(u => u.CanUsers).HasColumnName("can_users");
        modelBuilder.Entity<User>().Property(u => u.CanSettings).HasColumnName("can_settings");
        modelBuilder.Entity<User>().Property(u => u.CanSuppliers).HasColumnName("can_suppliers");
        modelBuilder.Entity<User>().Property(u => u.CanFleetManagement).HasColumnName("can_fleet_management");
        modelBuilder.Entity<User>().Property(u => u.CanTours).HasColumnName("can_tours");
        modelBuilder.Entity<User>().Property(u => u.CanFuel).HasColumnName("can_fuel");
        modelBuilder.Entity<User>().Property(u => u.CanPlayback).HasColumnName("can_playback");
        // Per-report user permission column mappings
        modelBuilder.Entity<User>().Property(u => u.CanReportTrips).HasColumnName("can_report_trips");
        modelBuilder.Entity<User>().Property(u => u.CanReportFuel).HasColumnName("can_report_fuel");
        modelBuilder.Entity<User>().Property(u => u.CanReportSpeed).HasColumnName("can_report_speed");
        modelBuilder.Entity<User>().Property(u => u.CanReportStops).HasColumnName("can_report_stops");
        modelBuilder.Entity<User>().Property(u => u.CanReportMileage).HasColumnName("can_report_mileage");
        modelBuilder.Entity<User>().Property(u => u.CanReportCosts).HasColumnName("can_report_costs");
        modelBuilder.Entity<User>().Property(u => u.CanReportMaintenance).HasColumnName("can_report_maintenance");
        modelBuilder.Entity<User>().Property(u => u.CanReportDaily).HasColumnName("can_report_daily");
        modelBuilder.Entity<User>().Property(u => u.CanReportMonthly).HasColumnName("can_report_monthly");
        modelBuilder.Entity<User>().Property(u => u.CanReportMileagePeriod).HasColumnName("can_report_mileage_period");
        modelBuilder.Entity<User>().Property(u => u.CanReportSpeedInfraction).HasColumnName("can_report_speed_infraction");
        modelBuilder.Entity<User>().Property(u => u.CanReportDrivingBehavior).HasColumnName("can_report_driving_behavior");

        // Role configuration is handled by RoleConfiguration.cs

        // Societe column mappings (match actual DB schema)
        modelBuilder.Entity<Societe>().Property(c => c.Id).HasColumnName("id");
        modelBuilder.Entity<Societe>().Property(c => c.Name).HasColumnName("name");
        modelBuilder.Entity<Societe>().Property(c => c.Type).HasColumnName("type");
        modelBuilder.Entity<Societe>().Property(c => c.Description).HasColumnName("description");
        modelBuilder.Entity<Societe>().Property(c => c.Address).HasColumnName("address");
        modelBuilder.Entity<Societe>().Property(c => c.City).HasColumnName("city");
        modelBuilder.Entity<Societe>().Property(c => c.Country).HasColumnName("country");
        modelBuilder.Entity<Societe>().Property(c => c.Phone).HasColumnName("phone");
        modelBuilder.Entity<Societe>().Property(c => c.Email).HasColumnName("email");
        modelBuilder.Entity<Societe>().Property(c => c.SubscriptionStartedAt).HasColumnName("subscription_started_at");
        modelBuilder.Entity<Societe>().Property(c => c.SubscriptionExpiresAt).HasColumnName("subscription_expires_at");
        modelBuilder.Entity<Societe>().Property(c => c.BillingCycle).HasColumnName("billing_cycle");
        modelBuilder.Entity<Societe>().Property(c => c.SubscriptionStatus).HasColumnName("subscription_status");
        modelBuilder.Entity<Societe>().Property(c => c.LastPaymentAt).HasColumnName("last_payment_at");
        modelBuilder.Entity<Societe>().Property(c => c.NextPaymentAmount).HasColumnName("next_payment_amount");
        modelBuilder.Entity<Societe>().Property(c => c.SubscriptionTypeId).HasColumnName("subscription_type_id");
        modelBuilder.Entity<Societe>().Property(c => c.IsActive).HasColumnName("is_active");
        modelBuilder.Entity<Societe>().Property(c => c.LogoUrl).HasColumnName("logo_url");
        modelBuilder.Entity<Societe>().Property(c => c.TaxId).HasColumnName("tax_id");
        modelBuilder.Entity<Societe>().Property(c => c.RC).HasColumnName("rc");
        modelBuilder.Entity<Societe>().Property(c => c.IF).HasColumnName("if");
        modelBuilder.Entity<Societe>().Property(c => c.CreatedAt).HasColumnName("created_at");
        modelBuilder.Entity<Societe>().Property(c => c.UpdatedAt).HasColumnName("updated_at");
        modelBuilder.Entity<Societe>().OwnsOne(c => c.Settings, b => b.ToJson("settings"));

        // User configuration is handled by UserConfiguration.cs

        // Column mappings for newly tenant-aware entities (CompanyId → company_id)
        modelBuilder.Entity<GpsAlert>().Property(e => e.CompanyId).HasColumnName("company_id");
        modelBuilder.Entity<VehicleMaintenanceSchedule>().Property(e => e.CompanyId).HasColumnName("company_id");
        modelBuilder.Entity<GeofenceEvent>().Property(e => e.CompanyId).HasColumnName("company_id");
        modelBuilder.Entity<MaintenanceLog>().Property(e => e.CompanyId).HasColumnName("company_id");

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

        // Tour decimal precision
        modelBuilder.Entity<Tour>().Property(t => t.EstimatedDistanceKm).HasPrecision(10, 2);
        modelBuilder.Entity<Tour>().Property(t => t.EstimatedFuelLiters).HasPrecision(10, 2);
        modelBuilder.Entity<Tour>().Property(t => t.ActualDistanceKm).HasPrecision(10, 2);
        modelBuilder.Entity<Tour>().Property(t => t.ActualFuelLiters).HasPrecision(10, 2);

        // ChatMessage configuration
        modelBuilder.Entity<ChatMessage>().Property(m => m.Id).HasColumnName("id");
        modelBuilder.Entity<ChatMessage>().Property(m => m.SenderId).HasColumnName("sender_id");
        modelBuilder.Entity<ChatMessage>().Property(m => m.ReceiverId).HasColumnName("receiver_id");
        modelBuilder.Entity<ChatMessage>().Property(m => m.Content).HasColumnName("content");
        modelBuilder.Entity<ChatMessage>().Property(m => m.IsRead).HasColumnName("is_read");
        modelBuilder.Entity<ChatMessage>().Property(m => m.ReadAt).HasColumnName("read_at");
        modelBuilder.Entity<ChatMessage>().Property(m => m.CompanyId).HasColumnName("company_id");
        modelBuilder.Entity<ChatMessage>().Property(m => m.CreatedAt).HasColumnName("created_at");
        modelBuilder.Entity<ChatMessage>().Property(m => m.UpdatedAt).HasColumnName("updated_at");
        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ChatMessage>()
            .HasOne(m => m.Receiver)
            .WithMany()
            .HasForeignKey(m => m.ReceiverId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => new { m.SenderId, m.ReceiverId, m.CreatedAt })
            .HasDatabaseName("ix_chat_messages_sender_receiver_time");
        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => new { m.ReceiverId, m.IsRead })
            .HasDatabaseName("ix_chat_messages_receiver_unread");

        // AiChatMessage configuration
        modelBuilder.Entity<AiChatMessage>().Property(m => m.Id).HasColumnName("id");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.UserId).HasColumnName("user_id");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.VehicleId).HasColumnName("vehicle_id");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.Role).HasColumnName("role").HasMaxLength(20);
        modelBuilder.Entity<AiChatMessage>().Property(m => m.Content).HasColumnName("content");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.SessionId).HasColumnName("session_id").HasMaxLength(100);
        modelBuilder.Entity<AiChatMessage>().Property(m => m.TokensUsed).HasColumnName("tokens_used");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.CompanyId).HasColumnName("company_id");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.CreatedAt).HasColumnName("created_at");
        modelBuilder.Entity<AiChatMessage>().Property(m => m.UpdatedAt).HasColumnName("updated_at");
        modelBuilder.Entity<AiChatMessage>()
            .HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AiChatMessage>()
            .HasOne(m => m.Vehicle)
            .WithMany()
            .HasForeignKey(m => m.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AiChatMessage>()
            .HasIndex(m => new { m.UserId, m.VehicleId, m.CreatedAt })
            .HasDatabaseName("ix_ai_chat_messages_user_vehicle_time");

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

        var result = await base.SaveChangesAsync(ct);

        // Dispatch domain events after successful save
        await DispatchDomainEventsAsync(ct);

        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        if (_publisher == null) return;

        var entities = ChangeTracker.Entries<Entity>()
            .Where(e => e.Entity.DomainEvents.Any())
            .Select(e => e.Entity)
            .ToList();

        var domainEvents = entities
            .SelectMany(e => e.DomainEvents)
            .ToList();

        // Clear events before publishing to avoid infinite loops
        foreach (var entity in entities)
            entity.ClearDomainEvents();

        foreach (var domainEvent in domainEvents)
            await _publisher.Publish(domainEvent, ct);
    }
}


