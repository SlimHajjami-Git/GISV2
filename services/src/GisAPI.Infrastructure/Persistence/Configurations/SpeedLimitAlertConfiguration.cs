using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class SpeedLimitAlertConfiguration : IEntityTypeConfiguration<SpeedLimitAlert>
{
    public void Configure(EntityTypeBuilder<SpeedLimitAlert> builder)
    {
        builder.ToTable("speed_limit_alerts");

        builder.HasKey(s => s.Id);

        builder.HasOne(s => s.Vehicle)
            .WithMany()
            .HasForeignKey(s => s.VehicleId);

        builder.HasOne(s => s.AcknowledgedBy)
            .WithMany()
            .HasForeignKey(s => s.AcknowledgedById);
    }
}


