using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Price).HasColumnName("price").HasColumnType("decimal(10,2)");
        builder.Property(e => e.Features).HasColumnName("features");
        builder.Property(e => e.GpsTracking).HasColumnName("gps_tracking");
        builder.Property(e => e.GpsInstallation).HasColumnName("gps_installation");
        builder.Property(e => e.MaxVehicles).HasColumnName("max_vehicles");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
    }
}
