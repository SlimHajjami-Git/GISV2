using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class VehicleStopConfiguration : IEntityTypeConfiguration<VehicleStop>
{
    public void Configure(EntityTypeBuilder<VehicleStop> builder)
    {
        builder.ToTable("vehicle_stops");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.DriverId).HasColumnName("driver_id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.StartTime).HasColumnName("start_time");
        builder.Property(e => e.EndTime).HasColumnName("end_time");
        builder.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(e => e.StopType).HasColumnName("stop_type").HasMaxLength(50);
        builder.Property(e => e.IgnitionOff).HasColumnName("ignition_off");
        builder.Property(e => e.IsAuthorized).HasColumnName("is_authorized");
        builder.Property(e => e.StartMileage).HasColumnName("start_mileage");
        builder.Property(e => e.EndMileage).HasColumnName("end_mileage");
        builder.Property(e => e.FuelLevelStart).HasColumnName("fuel_level_start");
        builder.Property(e => e.FuelLevelEnd).HasColumnName("fuel_level_end");
        builder.Property(e => e.GeofenceId).HasColumnName("geofence_id");
        builder.Property(e => e.InsideGeofence).HasColumnName("inside_geofence");
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.VehicleId, e.StartTime });
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId);

        builder.HasOne(e => e.Driver)
            .WithMany()
            .HasForeignKey(e => e.DriverId);

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId);

        builder.HasOne(e => e.Geofence)
            .WithMany()
            .HasForeignKey(e => e.GeofenceId);
    }
}
