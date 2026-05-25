using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class TowEventConfiguration : IEntityTypeConfiguration<TowEvent>
{
    public void Configure(EntityTypeBuilder<TowEvent> builder)
    {
        builder.ToTable("tow_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.DeviceUid).HasColumnName("device_uid").HasMaxLength(64);

        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(e => e.EndedAt).HasColumnName("ended_at");

        builder.Property(e => e.StartLat).HasColumnName("start_lat");
        builder.Property(e => e.StartLon).HasColumnName("start_lon");
        builder.Property(e => e.LastLat).HasColumnName("last_lat");
        builder.Property(e => e.LastLon).HasColumnName("last_lon");
        builder.Property(e => e.StartAddress).HasColumnName("start_address").HasMaxLength(512);

        builder.Property(e => e.MaxSpeedKph).HasColumnName("max_speed_kph");
        builder.Property(e => e.DistanceMeters).HasColumnName("distance_meters");
        builder.Property(e => e.FrameCount).HasColumnName("frame_count");

        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Acknowledged).HasColumnName("acknowledged");
        builder.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by");
        builder.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.DeviceId);
        builder.HasIndex(e => new { e.CompanyId, e.Status });
        builder.HasIndex(e => e.StartedAt).IsDescending();

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .IsRequired(false);

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId)
            .IsRequired(false);
    }
}
