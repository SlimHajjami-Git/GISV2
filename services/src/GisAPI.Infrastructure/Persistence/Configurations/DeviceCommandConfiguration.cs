using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class DeviceCommandConfiguration : IEntityTypeConfiguration<DeviceCommand>
{
    public void Configure(EntityTypeBuilder<DeviceCommand> builder)
    {
        builder.ToTable("device_commands");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.CommandType).HasColumnName("command_type").HasMaxLength(20).IsRequired();
        builder.Property(e => e.CommandText).HasColumnName("command_text").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
        builder.Property(e => e.SentAt).HasColumnName("sent_at");
        builder.Property(e => e.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("manual");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.DeviceId, e.Status });
        builder.HasIndex(e => e.CreatedAt).IsDescending();

        builder.HasOne(e => e.Device)
            .WithMany(d => d.Commands)
            .HasForeignKey(e => e.DeviceId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .IsRequired(false);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId);
    }
}
