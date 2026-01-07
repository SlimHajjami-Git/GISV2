using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class GeofenceConfiguration : IEntityTypeConfiguration<Geofence>
{
    public void Configure(EntityTypeBuilder<Geofence> builder)
    {
        builder.ToTable("geofences");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(20);
        builder.Property(e => e.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(e => e.IconName).HasColumnName("icon_name").HasMaxLength(50);
        builder.Property(e => e.Coordinates).HasColumnName("coordinates").HasColumnType("jsonb");
        builder.Property(e => e.CenterLat).HasColumnName("center_lat");
        builder.Property(e => e.CenterLng).HasColumnName("center_lng");
        builder.Property(e => e.Radius).HasColumnName("radius");
        builder.Property(e => e.AlertOnEntry).HasColumnName("alert_on_entry");
        builder.Property(e => e.AlertOnExit).HasColumnName("alert_on_exit");
        builder.Property(e => e.AlertSpeedLimit).HasColumnName("alert_speed_limit");
        builder.Property(e => e.NotificationCooldownMinutes).HasColumnName("notification_cooldown_minutes");
        builder.Property(e => e.MaxStayDurationMinutes).HasColumnName("max_stay_duration_minutes");
        builder.Property(e => e.ActiveStartTime).HasColumnName("active_start_time");
        builder.Property(e => e.ActiveEndTime).HasColumnName("active_end_time");
        builder.Property(e => e.ActiveDays).HasColumnName("active_days").HasColumnType("jsonb");
        builder.Property(e => e.IsActive).HasColumnName("is_active");
        builder.Property(e => e.GroupId).HasColumnName("group_id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Company)
            .WithMany(c => c.Geofences)
            .HasForeignKey(e => e.CompanyId);

        builder.HasOne(e => e.Group)
            .WithMany(g => g.Geofences)
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class GeofenceVehicleConfiguration : IEntityTypeConfiguration<GeofenceVehicle>
{
    public void Configure(EntityTypeBuilder<GeofenceVehicle> builder)
    {
        builder.ToTable("geofence_vehicles");

        builder.HasKey(e => new { e.GeofenceId, e.VehicleId });
        builder.Property(e => e.GeofenceId).HasColumnName("geofence_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.AssignedAt).HasColumnName("assigned_at");

        builder.HasOne(e => e.Geofence)
            .WithMany(g => g.AssignedVehicles)
            .HasForeignKey(e => e.GeofenceId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId);
    }
}

public class GeofenceEventConfiguration : IEntityTypeConfiguration<GeofenceEvent>
{
    public void Configure(EntityTypeBuilder<GeofenceEvent> builder)
    {
        builder.ToTable("geofence_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.GeofenceId).HasColumnName("geofence_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(30).IsRequired();
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(e => e.Speed).HasColumnName("speed");
        builder.Property(e => e.DurationInsideSeconds).HasColumnName("duration_inside_seconds");
        builder.Property(e => e.IsNotified).HasColumnName("is_notified");
        builder.Property(e => e.NotifiedAt).HasColumnName("notified_at");
        builder.Property(e => e.Timestamp).HasColumnName("timestamp");

        builder.HasIndex(e => new { e.GeofenceId, e.Timestamp });
        builder.HasIndex(e => new { e.VehicleId, e.Timestamp });

        builder.HasOne(e => e.Geofence)
            .WithMany(g => g.Events)
            .HasForeignKey(e => e.GeofenceId);
    }
}

public class GeofenceGroupConfiguration : IEntityTypeConfiguration<GeofenceGroup>
{
    public void Configure(EntityTypeBuilder<GeofenceGroup> builder)
    {
        builder.ToTable("geofence_groups");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(e => e.IconName).HasColumnName("icon_name").HasMaxLength(50);
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Company)
            .WithMany()
            .HasForeignKey(e => e.CompanyId);
    }
}
