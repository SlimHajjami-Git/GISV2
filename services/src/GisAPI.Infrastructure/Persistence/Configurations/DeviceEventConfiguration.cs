using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> builder)
    {
        builder.ToTable("device_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.EventAt).HasColumnName("event_at");
        builder.Property(e => e.OfflineDurationSecs).HasColumnName("offline_duration_secs");
        builder.Property(e => e.LastKnownLat).HasColumnName("last_known_lat");
        builder.Property(e => e.LastKnownLon).HasColumnName("last_known_lon");
        builder.Property(e => e.LastKnownAddress).HasColumnName("last_known_address");
        builder.Property(e => e.WasMoving).HasColumnName("was_moving");
        builder.Property(e => e.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(e => e.Acknowledged).HasColumnName("acknowledged");
        builder.Property(e => e.AcknowledgedBy).HasColumnName("acknowledged_by");
        builder.Property(e => e.AcknowledgedAt).HasColumnName("acknowledged_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        // updated_at column does not exist in DB — ignore it
        builder.Ignore(e => e.UpdatedAt);

        builder.HasIndex(e => e.DeviceId);
        builder.HasIndex(e => new { e.CompanyId, e.Acknowledged });
        builder.HasIndex(e => e.EventAt).IsDescending();

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .IsRequired(false);

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId);
    }
}
