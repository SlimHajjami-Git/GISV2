using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

/// <summary>Table gps_device_blocklist (migration 057) : IMEI des boîtiers supprimés.</summary>
public class GpsDeviceBlocklistEntryConfiguration : IEntityTypeConfiguration<GpsDeviceBlocklistEntry>
{
    public void Configure(EntityTypeBuilder<GpsDeviceBlocklistEntry> builder)
    {
        builder.ToTable("gps_device_blocklist");

        builder.HasKey(e => e.DeviceUid);
        builder.Property(e => e.DeviceUid).HasColumnName("device_uid").HasMaxLength(100).IsRequired();
        builder.Property(e => e.BlockedAt).HasColumnName("blocked_at").IsRequired();
        builder.Property(e => e.BlockedByUserId).HasColumnName("blocked_by_user_id");
        builder.Property(e => e.DeviceIdBefore).HasColumnName("device_id_before");
        builder.Property(e => e.Mat).HasColumnName("mat").HasMaxLength(50);
        builder.Property(e => e.CompanyIdBefore).HasColumnName("company_id_before");
        builder.Property(e => e.Reason).HasColumnName("reason");
    }
}
