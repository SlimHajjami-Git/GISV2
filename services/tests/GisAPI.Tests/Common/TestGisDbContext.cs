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
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierService> SupplierServices => Set<SupplierService>();
    public DbSet<AccidentClaim> AccidentClaims => Set<AccidentClaim>();
    public DbSet<AccidentClaimThirdParty> AccidentClaimThirdParties => Set<AccidentClaimThirdParty>();
    public DbSet<AccidentClaimDocument> AccidentClaimDocuments => Set<AccidentClaimDocument>();
    public DbSet<MaintenanceTemplate> MaintenanceTemplates => Set<MaintenanceTemplate>();
    public DbSet<VehicleMaintenanceSchedule> VehicleMaintenanceSchedules => Set<VehicleMaintenanceSchedule>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    
    // Fleet Management
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<FuelType> FuelTypes => Set<FuelType>();
    public DbSet<FuelPricing> FuelPricings => Set<FuelPricing>();
    public DbSet<SpeedLimitAlert> SpeedLimitAlerts => Set<SpeedLimitAlert>();
    
    // Brands & Models
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<VehicleModel> VehicleModels => Set<VehicleModel>();
    public DbSet<PartCategory> PartCategories => Set<PartCategory>();
    public DbSet<VehiclePart> VehicleParts => Set<VehiclePart>();
    public DbSet<PartPricing> PartPricings => Set<PartPricing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Minimal configuration for testing - ignore complex types
        modelBuilder.Entity<User>().Ignore(u => u.FullName).Ignore(u => u.IsCompanyAdmin);
        modelBuilder.Entity<Geofence>().Ignore(g => g.Coordinates);
        modelBuilder.Entity<Societe>().Ignore(c => c.Settings);
        
        // Configure owned types as ignored for InMemory
        modelBuilder.Entity<MaintenanceRecord>().Ignore(m => m.Parts);
        modelBuilder.Entity<GpsDevice>().Ignore(d => d.Positions).Ignore(d => d.Alerts);
        modelBuilder.Entity<Geofence>().Ignore(g => g.Events);
        modelBuilder.Entity<Vehicle>().Ignore(v => v.Documents);
        
        // Ignore navigation collections that cause InMemory issues
        modelBuilder.Entity<AccidentClaim>().Ignore(a => a.ThirdParties).Ignore(a => a.Documents);
        modelBuilder.Entity<GpsPosition>().Ignore(p => p.Metadata);
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


