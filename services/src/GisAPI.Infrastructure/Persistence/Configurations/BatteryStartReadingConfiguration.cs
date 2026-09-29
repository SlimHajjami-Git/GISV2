using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class BatteryStartReadingConfiguration : IEntityTypeConfiguration<BatteryStartReading>
{
    public void Configure(EntityTypeBuilder<BatteryStartReading> builder)
    {
        builder.ToTable("battery_start_readings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id").IsRequired();
        builder.Property(e => e.StartAt).HasColumnName("start_at").IsRequired();
        builder.Property(e => e.BatteryRaw).HasColumnName("battery_raw").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        // Clé d'idempotence : un démarrage est vu par plusieurs cycles du service.
        builder.HasIndex(e => new { e.DeviceId, e.StartAt }).IsUnique();

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
