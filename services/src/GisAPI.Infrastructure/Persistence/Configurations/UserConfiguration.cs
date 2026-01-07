using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(150).IsRequired();
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.PasswordHash).HasColumnName("password_hash").IsRequired();
        builder.Property(e => e.Roles).HasColumnName("roles");
        builder.Property(e => e.Permissions).HasColumnName("permissions");
        builder.Property(e => e.AssignedVehicleIds).HasColumnName("assigned_vehicle_ids");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(e => e.UserSettingsId).HasColumnName("user_settings_id");

        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(e => e.CompanyId);

        builder.HasOne(e => e.Settings)
            .WithOne()
            .HasForeignKey<User>(e => e.UserSettingsId);
    }
}

public class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Language).HasColumnName("language").HasMaxLength(10);
        builder.Property(e => e.Timezone).HasColumnName("timezone").HasMaxLength(50);
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(10);
        builder.Property(e => e.DateFormat).HasColumnName("date_format").HasMaxLength(20);
        builder.Property(e => e.DistanceUnit).HasColumnName("distance_unit").HasMaxLength(10);
        builder.Property(e => e.SpeedUnit).HasColumnName("speed_unit").HasMaxLength(10);
        builder.Property(e => e.VolumeUnit).HasColumnName("volume_unit").HasMaxLength(10);
        builder.Property(e => e.TemperatureUnit).HasColumnName("temperature_unit").HasMaxLength(5);
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.OwnsOne(e => e.Notifications, n => n.ToJson("notifications"));
        builder.OwnsOne(e => e.Display, d => d.ToJson("display"));
    }
}
