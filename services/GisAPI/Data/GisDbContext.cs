using Microsoft.EntityFrameworkCore;
using GisAPI.Models;

namespace GisAPI.Data;

public class GisDbContext : DbContext
{
    public GisDbContext(DbContextOptions<GisDbContext> options) : base(options)
    {
    }

    // ============================================
    // CORE ENTITIES
    // ============================================
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    // ============================================
    // VEHICLES & EMPLOYEES
    // ============================================
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<VehicleDocument> VehicleDocuments { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<DriverAssignment> DriverAssignments { get; set; }
    public DbSet<DriverScore> DriverScores { get; set; }
    public DbSet<UserVehicle> UserVehicles { get; set; }

    // ============================================
    // GPS & TRACKING
    // ============================================
    public DbSet<GpsDevice> GpsDevices { get; set; }
    public DbSet<GpsPosition> GpsPositions { get; set; }
    public DbSet<GpsAlert> GpsAlerts { get; set; }

    // ============================================
    // TRIPS & DRIVING BEHAVIOR
    // ============================================
    public DbSet<Trip> Trips { get; set; }
    public DbSet<TripWaypoint> TripWaypoints { get; set; }
    public DbSet<DrivingEvent> DrivingEvents { get; set; }
    public DbSet<DailyStatistics> DailyStatistics { get; set; }

    // ============================================
    // GEOFENCES & POI
    // ============================================
    public DbSet<Geofence> Geofences { get; set; }
    public DbSet<GeofenceVehicle> GeofenceVehicles { get; set; }
    public DbSet<GeofenceEvent> GeofenceEvents { get; set; }
    public DbSet<GeofenceGroup> GeofenceGroups { get; set; }
    public DbSet<PointOfInterest> PointsOfInterest { get; set; }
    public DbSet<PoiVisit> PoiVisits { get; set; }

    // ============================================
    // MAINTENANCE & COSTS
    // ============================================
    public DbSet<MaintenanceRecord> MaintenanceRecords { get; set; }
    public DbSet<MaintenancePart> MaintenanceParts { get; set; }
    public DbSet<VehicleCost> VehicleCosts { get; set; }

    // ============================================
    // CONTRACTS & RESERVATIONS
    // ============================================
    public DbSet<Contract> Contracts { get; set; }
    public DbSet<Reservation> Reservations { get; set; }

    // ============================================
    // INVENTORY & SUPPLIERS
    // ============================================
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<PartInventory> PartInventory { get; set; }
    public DbSet<PartTransaction> PartTransactions { get; set; }

    // ============================================
    // REPORTS & NOTIFICATIONS
    // ============================================
    public DbSet<Report> Reports { get; set; }
    public DbSet<ReportSchedule> ReportSchedules { get; set; }
    public DbSet<Notification> Notifications { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ============================================
        // TABLE NAMING (snake_case for PostgreSQL)
        // ============================================
        modelBuilder.Entity<Subscription>().ToTable("subscriptions");
        modelBuilder.Entity<Company>().ToTable("companies");
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<UserSettings>().ToTable("user_settings");
        modelBuilder.Entity<RefreshToken>().ToTable("refresh_tokens");
        modelBuilder.Entity<Vehicle>().ToTable("vehicles");
        modelBuilder.Entity<VehicleDocument>().ToTable("vehicle_documents");
        modelBuilder.Entity<Employee>().ToTable("employees");
        modelBuilder.Entity<DriverAssignment>().ToTable("driver_assignments");
        modelBuilder.Entity<DriverScore>().ToTable("driver_scores");
        modelBuilder.Entity<UserVehicle>().ToTable("user_vehicles");
        modelBuilder.Entity<GpsDevice>().ToTable("gps_devices");
        modelBuilder.Entity<GpsPosition>().ToTable("gps_positions");
        modelBuilder.Entity<GpsAlert>().ToTable("gps_alerts");
        modelBuilder.Entity<Trip>().ToTable("trips");
        modelBuilder.Entity<TripWaypoint>().ToTable("trip_waypoints");
        modelBuilder.Entity<DrivingEvent>().ToTable("driving_events");
        modelBuilder.Entity<DailyStatistics>().ToTable("daily_statistics");
        modelBuilder.Entity<Geofence>().ToTable("geofences");
        modelBuilder.Entity<GeofenceVehicle>().ToTable("geofence_vehicles");
        modelBuilder.Entity<GeofenceEvent>().ToTable("geofence_events");
        modelBuilder.Entity<GeofenceGroup>().ToTable("geofence_groups");
        modelBuilder.Entity<PointOfInterest>().ToTable("points_of_interest");
        modelBuilder.Entity<PoiVisit>().ToTable("poi_visits");
        modelBuilder.Entity<MaintenanceRecord>().ToTable("maintenance_records");
        modelBuilder.Entity<MaintenancePart>().ToTable("maintenance_parts");
        modelBuilder.Entity<VehicleCost>().ToTable("vehicle_costs");
        modelBuilder.Entity<Contract>().ToTable("contracts");
        modelBuilder.Entity<Reservation>().ToTable("reservations");
        modelBuilder.Entity<Supplier>().ToTable("suppliers");
        modelBuilder.Entity<PartInventory>().ToTable("part_inventory");
        modelBuilder.Entity<PartTransaction>().ToTable("part_transactions");
        modelBuilder.Entity<Report>().ToTable("reports");
        modelBuilder.Entity<ReportSchedule>().ToTable("report_schedules");
        modelBuilder.Entity<Notification>().ToTable("notifications");
        modelBuilder.Entity<AuditLog>().ToTable("audit_logs");

        // ============================================
        // COMPOSITE KEYS
        // ============================================
        
        // GeofenceVehicle pivot table
        modelBuilder.Entity<GeofenceVehicle>()
            .HasKey(gv => new { gv.GeofenceId, gv.VehicleId });

        // UserVehicle pivot table
        modelBuilder.Entity<UserVehicle>()
            .HasKey(uv => new { uv.UserId, uv.VehicleId });

        // ============================================
        // RELATIONSHIPS - GEOFENCE
        // ============================================
        modelBuilder.Entity<GeofenceVehicle>()
            .HasOne(gv => gv.Geofence)
            .WithMany(g => g.AssignedVehicles)
            .HasForeignKey(gv => gv.GeofenceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeofenceVehicle>()
            .HasOne(gv => gv.Vehicle)
            .WithMany()
            .HasForeignKey(gv => gv.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<GeofenceEvent>()
            .HasOne(ge => ge.Geofence)
            .WithMany(g => g.Events)
            .HasForeignKey(ge => ge.GeofenceId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Geofence>()
            .HasOne(g => g.Group)
            .WithMany(gg => gg.Geofences)
            .HasForeignKey(g => g.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // ============================================
        // RELATIONSHIPS - POI VISITS
        // ============================================
        modelBuilder.Entity<PoiVisit>()
            .HasOne(pv => pv.Poi)
            .WithMany(p => p.Visits)
            .HasForeignKey(pv => pv.PoiId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PoiVisit>()
            .HasOne(pv => pv.Vehicle)
            .WithMany()
            .HasForeignKey(pv => pv.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // RELATIONSHIPS - USER VEHICLE
        // ============================================
        modelBuilder.Entity<UserVehicle>()
            .HasOne(uv => uv.User)
            .WithMany(u => u.AssignedVehicles)
            .HasForeignKey(uv => uv.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserVehicle>()
            .HasOne(uv => uv.Vehicle)
            .WithMany(v => v.AssignedUsers)
            .HasForeignKey(uv => uv.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // RELATIONSHIPS - VEHICLE & EMPLOYEE
        // ============================================
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.AssignedDriver)
            .WithMany(e => e.AssignedVehiclesAsDriver)
            .HasForeignKey(v => v.AssignedDriverId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.AssignedSupervisor)
            .WithMany(e => e.AssignedVehiclesAsSupervisor)
            .HasForeignKey(v => v.AssignedSupervisorId)
            .OnDelete(DeleteBehavior.SetNull);

        // Vehicle -> GpsDevice one-to-one
        modelBuilder.Entity<Vehicle>()
            .HasOne(v => v.GpsDevice)
            .WithOne(g => g.Vehicle)
            .HasForeignKey<Vehicle>(v => v.GpsDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        // ============================================
        // RELATIONSHIPS - DRIVER ASSIGNMENT
        // ============================================
        modelBuilder.Entity<DriverAssignment>()
            .HasOne(da => da.Vehicle)
            .WithMany(v => v.DriverAssignments)
            .HasForeignKey(da => da.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DriverAssignment>()
            .HasOne(da => da.Driver)
            .WithMany(e => e.DriverAssignments)
            .HasForeignKey(da => da.DriverId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // RELATIONSHIPS - TRIPS
        // ============================================
        modelBuilder.Entity<Trip>()
            .HasOne(t => t.Vehicle)
            .WithMany(v => v.Trips)
            .HasForeignKey(t => t.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Trip>()
            .HasOne(t => t.Driver)
            .WithMany(e => e.Trips)
            .HasForeignKey(t => t.DriverId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TripWaypoint>()
            .HasOne(tw => tw.Trip)
            .WithMany(t => t.Waypoints)
            .HasForeignKey(tw => tw.TripId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // RELATIONSHIPS - DRIVING EVENTS
        // ============================================
        modelBuilder.Entity<DrivingEvent>()
            .HasOne(de => de.Vehicle)
            .WithMany(v => v.DrivingEvents)
            .HasForeignKey(de => de.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DrivingEvent>()
            .HasOne(de => de.Trip)
            .WithMany()
            .HasForeignKey(de => de.TripId)
            .OnDelete(DeleteBehavior.SetNull);

        // ============================================
        // RELATIONSHIPS - CONTRACTS & RESERVATIONS
        // ============================================
        modelBuilder.Entity<Contract>()
            .HasOne(c => c.Vehicle)
            .WithMany(v => v.Contracts)
            .HasForeignKey(c => c.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.Vehicle)
            .WithMany(v => v.Reservations)
            .HasForeignKey(r => r.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // RELATIONSHIPS - INVENTORY
        // ============================================
        modelBuilder.Entity<PartInventory>()
            .HasOne(pi => pi.Supplier)
            .WithMany(s => s.Parts)
            .HasForeignKey(pi => pi.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<PartTransaction>()
            .HasOne(pt => pt.Part)
            .WithMany()
            .HasForeignKey(pt => pt.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        // ============================================
        // INDEXES - PERFORMANCE
        // ============================================
        
        // GPS Positions - high volume table
        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => p.DeviceId)
            .HasDatabaseName("idx_gps_positions_device_id");

        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => p.RecordedAt)
            .IsDescending()
            .HasDatabaseName("idx_gps_positions_recorded_at");

        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => new { p.DeviceId, p.RecordedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_gps_positions_device_recorded");

        // Vehicles
        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.CompanyId)
            .HasDatabaseName("idx_vehicles_company_id");

        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.Plate)
            .HasDatabaseName("idx_vehicles_plate_number");

        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.Status)
            .HasDatabaseName("idx_vehicles_status");

        // Users
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("idx_users_email_unique");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.CompanyId)
            .HasDatabaseName("idx_users_company_id");

        // GPS Devices
        modelBuilder.Entity<GpsDevice>()
            .HasIndex(d => d.DeviceUid)
            .IsUnique()
            .HasDatabaseName("idx_gps_devices_uid_unique");

        modelBuilder.Entity<GpsDevice>()
            .HasIndex(d => d.CompanyId)
            .HasDatabaseName("idx_gps_devices_company_id");

        // GPS Alerts
        modelBuilder.Entity<GpsAlert>()
            .HasIndex(a => a.Timestamp)
            .IsDescending()
            .HasDatabaseName("idx_gps_alerts_timestamp");

        modelBuilder.Entity<GpsAlert>()
            .HasIndex(a => new { a.VehicleId, a.Resolved })
            .HasDatabaseName("idx_gps_alerts_vehicle_resolved");

        // Geofences
        modelBuilder.Entity<Geofence>()
            .HasIndex(g => g.CompanyId)
            .HasDatabaseName("idx_geofences_company_id");

        modelBuilder.Entity<GeofenceEvent>()
            .HasIndex(ge => new { ge.GeofenceId, ge.Timestamp })
            .IsDescending(false, true)
            .HasDatabaseName("idx_geofence_events_geofence_timestamp");

        modelBuilder.Entity<GeofenceEvent>()
            .HasIndex(ge => new { ge.VehicleId, ge.Timestamp })
            .IsDescending(false, true)
            .HasDatabaseName("idx_geofence_events_vehicle_timestamp");

        modelBuilder.Entity<GeofenceGroup>()
            .HasIndex(gg => gg.CompanyId)
            .HasDatabaseName("idx_geofence_groups_company_id");

        // POI & Visits
        modelBuilder.Entity<PointOfInterest>()
            .HasIndex(p => p.CompanyId)
            .HasDatabaseName("idx_poi_company_id");

        modelBuilder.Entity<PoiVisit>()
            .HasIndex(pv => new { pv.PoiId, pv.ArrivalAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_poi_visits_poi_arrival");

        modelBuilder.Entity<PoiVisit>()
            .HasIndex(pv => new { pv.VehicleId, pv.ArrivalAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_poi_visits_vehicle_arrival");

        // Trips
        modelBuilder.Entity<Trip>()
            .HasIndex(t => t.VehicleId)
            .HasDatabaseName("idx_trips_vehicle_id");

        modelBuilder.Entity<Trip>()
            .HasIndex(t => t.StartTime)
            .IsDescending()
            .HasDatabaseName("idx_trips_start_time");

        modelBuilder.Entity<Trip>()
            .HasIndex(t => new { t.CompanyId, t.StartTime })
            .IsDescending(false, true)
            .HasDatabaseName("idx_trips_company_start_time");

        // Daily Statistics
        modelBuilder.Entity<DailyStatistics>()
            .HasIndex(ds => new { ds.VehicleId, ds.Date })
            .IsUnique()
            .HasDatabaseName("idx_daily_statistics_vehicle_date_unique");

        modelBuilder.Entity<DailyStatistics>()
            .HasIndex(ds => ds.Date)
            .HasDatabaseName("idx_daily_statistics_date");

        // Driver Scores
        modelBuilder.Entity<DriverScore>()
            .HasIndex(ds => new { ds.DriverId, ds.Date })
            .IsUnique()
            .HasDatabaseName("idx_driver_scores_driver_date_unique");

        // Notifications
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.IsRead })
            .HasDatabaseName("idx_notifications_user_read");

        modelBuilder.Entity<Notification>()
            .HasIndex(n => n.CreatedAt)
            .IsDescending()
            .HasDatabaseName("idx_notifications_created_at");

        // Audit Logs
        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp)
            .IsDescending()
            .HasDatabaseName("idx_audit_logs_timestamp");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("idx_audit_logs_entity");

        // Refresh Tokens
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.Token)
            .HasDatabaseName("idx_refresh_tokens_token");

        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.UserId)
            .HasDatabaseName("idx_refresh_tokens_user_id");

        // Maintenance Records
        modelBuilder.Entity<MaintenanceRecord>()
            .HasIndex(mr => mr.VehicleId)
            .HasDatabaseName("idx_maintenance_records_vehicle_id");

        modelBuilder.Entity<MaintenanceRecord>()
            .HasIndex(mr => mr.Date)
            .HasDatabaseName("idx_maintenance_records_date");

        // Vehicle Costs
        modelBuilder.Entity<VehicleCost>()
            .HasIndex(vc => vc.VehicleId)
            .HasDatabaseName("idx_vehicle_costs_vehicle_id");

        modelBuilder.Entity<VehicleCost>()
            .HasIndex(vc => vc.Date)
            .HasDatabaseName("idx_vehicle_costs_date");

        // Contracts
        modelBuilder.Entity<Contract>()
            .HasIndex(c => c.EndDate)
            .HasDatabaseName("idx_contracts_end_date");

        // Reservations
        modelBuilder.Entity<Reservation>()
            .HasIndex(r => new { r.VehicleId, r.StartDateTime })
            .HasDatabaseName("idx_reservations_vehicle_start");

        // Part Inventory
        modelBuilder.Entity<PartInventory>()
            .HasIndex(pi => pi.PartNumber)
            .HasDatabaseName("idx_part_inventory_part_number");

        // Driving Events
        modelBuilder.Entity<DrivingEvent>()
            .HasIndex(de => de.Timestamp)
            .IsDescending()
            .HasDatabaseName("idx_driving_events_timestamp");

        modelBuilder.Entity<DrivingEvent>()
            .HasIndex(de => new { de.VehicleId, de.Timestamp })
            .IsDescending(false, true)
            .HasDatabaseName("idx_driving_events_vehicle_timestamp");

        // ============================================
        // JSON COLUMN CONFIGURATION (PostgreSQL)
        // ============================================
        modelBuilder.Entity<Company>()
            .OwnsOne(c => c.Settings, builder => builder.ToJson());

        modelBuilder.Entity<UserSettings>()
            .OwnsOne(s => s.Notifications, builder => builder.ToJson());

        modelBuilder.Entity<UserSettings>()
            .OwnsOne(s => s.Display, builder => builder.ToJson());

        modelBuilder.Entity<Geofence>()
            .Property(g => g.Coordinates)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Geofence>()
            .Property(g => g.ActiveDays)
            .HasColumnType("jsonb");

        modelBuilder.Entity<PointOfInterest>()
            .Property(p => p.Tags)
            .HasColumnType("jsonb");

        modelBuilder.Entity<PointOfInterest>()
            .OwnsOne(p => p.Hours, builder => builder.ToJson());

        modelBuilder.Entity<GpsPosition>()
            .Property(p => p.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Notification>()
            .Property(n => n.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.OldValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.NewValues)
            .HasColumnType("jsonb");

        modelBuilder.Entity<DrivingEvent>()
            .Property(de => de.Metadata)
            .HasColumnType("jsonb");

        modelBuilder.Entity<PointOfInterest>()
            .OwnsOne(p => p.Hours, builder => builder.ToJson());

        // ============================================
        // ARRAY COLUMN CONFIGURATION (PostgreSQL)
        // ============================================
        modelBuilder.Entity<User>()
            .Property(u => u.Roles)
            .HasColumnType("text[]");

        modelBuilder.Entity<User>()
            .Property(u => u.Permissions)
            .HasColumnType("text[]");

        modelBuilder.Entity<User>()
            .Property(u => u.AssignedVehicleIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<Subscription>()
            .Property(s => s.Features)
            .HasColumnType("text[]");

        modelBuilder.Entity<Report>()
            .Property(r => r.VehicleIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<Report>()
            .Property(r => r.DriverIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(rs => rs.EmailRecipients)
            .HasColumnType("text[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(rs => rs.VehicleIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<ReportSchedule>()
            .Property(rs => rs.DriverIds)
            .HasColumnType("integer[]");

        modelBuilder.Entity<PartInventory>()
            .Property(pi => pi.CompatibleVehicles)
            .HasColumnType("text[]");

        // ============================================
        // DECIMAL PRECISION
        // ============================================
        modelBuilder.Entity<Subscription>()
            .Property(s => s.Price)
            .HasPrecision(10, 2);

        modelBuilder.Entity<MaintenanceRecord>()
            .Property(m => m.LaborCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<MaintenanceRecord>()
            .Property(m => m.PartsCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<MaintenanceRecord>()
            .Property(m => m.TotalCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<MaintenancePart>()
            .Property(mp => mp.UnitCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<MaintenancePart>()
            .Property(mp => mp.TotalCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<VehicleCost>()
            .Property(vc => vc.Amount)
            .HasPrecision(10, 2);

        modelBuilder.Entity<VehicleCost>()
            .Property(vc => vc.Liters)
            .HasPrecision(10, 2);

        modelBuilder.Entity<VehicleCost>()
            .Property(vc => vc.PricePerLiter)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Trip>()
            .Property(t => t.DistanceKm)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Trip>()
            .Property(t => t.FuelConsumedLiters)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Trip>()
            .Property(t => t.AverageSpeedKph)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Trip>()
            .Property(t => t.MaxSpeedKph)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.DistanceKm)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.AverageSpeedKph)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.MaxSpeedKph)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.FuelConsumedLiters)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.FuelEfficiencyKmPerLiter)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DailyStatistics>()
            .Property(ds => ds.FuelCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DriverScore>()
            .Property(ds => ds.DistanceKm)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Contract>()
            .Property(c => c.TotalValue)
            .HasPrecision(12, 2);

        modelBuilder.Entity<Contract>()
            .Property(c => c.MonthlyPayment)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Contract>()
            .Property(c => c.ExcessKmRate)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Contract>()
            .Property(c => c.ResidualValue)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Contract>()
            .Property(c => c.Deductible)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Supplier>()
            .Property(s => s.DiscountPercent)
            .HasPrecision(5, 2);

        modelBuilder.Entity<PartInventory>()
            .Property(pi => pi.UnitCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<PartInventory>()
            .Property(pi => pi.SellingPrice)
            .HasPrecision(10, 2);

        modelBuilder.Entity<PartTransaction>()
            .Property(pt => pt.UnitCost)
            .HasPrecision(10, 2);

        modelBuilder.Entity<PartTransaction>()
            .Property(pt => pt.TotalCost)
            .HasPrecision(10, 2);
    }
}
