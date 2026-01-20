using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Tests.Common;

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
    public DbSet<Societe> Societes => Set<Societe>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Minimal configuration for testing - ignore complex types
        modelBuilder.Entity<User>().Ignore(u => u.Roles).Ignore(u => u.Permissions);
        modelBuilder.Entity<Geofence>().Ignore(g => g.Coordinates);
        modelBuilder.Entity<Societe>().Ignore(c => c.Settings);
        
        // Configure owned types as ignored for InMemory
        modelBuilder.Entity<User>().Ignore(u => u.Settings);
        modelBuilder.Entity<MaintenanceRecord>().Ignore(m => m.Parts);
        modelBuilder.Entity<GpsDevice>().Ignore(d => d.Positions).Ignore(d => d.Alerts);
        modelBuilder.Entity<Geofence>().Ignore(g => g.Events);
        modelBuilder.Entity<Vehicle>().Ignore(v => v.Documents);
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
