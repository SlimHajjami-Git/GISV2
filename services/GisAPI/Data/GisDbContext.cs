using Microsoft.EntityFrameworkCore;
using GisAPI.Models;

namespace GisAPI.Data;

public class GisDbContext : DbContext
{
    public GisDbContext(DbContextOptions<GisDbContext> options) : base(options)
    {
    }

    // Core
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }

    // Vehicles & Employees
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<VehicleDocument> VehicleDocuments { get; set; }
    public DbSet<Employee> Employees { get; set; }

    // GPS
    public DbSet<GpsDevice> GpsDevices { get; set; }
    public DbSet<GpsPosition> GpsPositions { get; set; }
    public DbSet<GpsAlert> GpsAlerts { get; set; }

    // Geofences
    public DbSet<Geofence> Geofences { get; set; }
    public DbSet<GeofenceVehicle> GeofenceVehicles { get; set; }
    public DbSet<GeofenceEvent> GeofenceEvents { get; set; }

    // Maintenance & Costs
    public DbSet<MaintenanceRecord> MaintenanceRecords { get; set; }
    public DbSet<MaintenancePart> MaintenanceParts { get; set; }
    public DbSet<VehicleCost> VehicleCosts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Composite key for GeofenceVehicle pivot table
        modelBuilder.Entity<GeofenceVehicle>()
            .HasKey(gv => new { gv.GeofenceId, gv.VehicleId });

        modelBuilder.Entity<GeofenceVehicle>()
            .HasOne(gv => gv.Geofence)
            .WithMany(g => g.AssignedVehicles)
            .HasForeignKey(gv => gv.GeofenceId);

        modelBuilder.Entity<GeofenceVehicle>()
            .HasOne(gv => gv.Vehicle)
            .WithMany()
            .HasForeignKey(gv => gv.VehicleId);

        // Vehicle -> Employee relationships
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

        // Indexes for performance
        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => p.DeviceId);

        modelBuilder.Entity<GpsPosition>()
            .HasIndex(p => p.RecordedAt)
            .IsDescending();

        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.CompanyId);

        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.Plate)
            .HasDatabaseName("idx_vehicles_plate_number");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<GpsDevice>()
            .HasIndex(d => d.DeviceUid)
            .IsUnique();

        modelBuilder.Entity<GpsAlert>()
            .HasIndex(a => a.Timestamp)
            .IsDescending();

        modelBuilder.Entity<Geofence>()
            .HasIndex(g => g.CompanyId);

        // JSON column configuration for PostgreSQL
        modelBuilder.Entity<Company>()
            .OwnsOne(c => c.Settings, builder => builder.ToJson());

        modelBuilder.Entity<UserSettings>()
            .OwnsOne(s => s.Notifications, builder => builder.ToJson());

        modelBuilder.Entity<UserSettings>()
            .OwnsOne(s => s.Display, builder => builder.ToJson());
    }
}
