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
        
        builder.Property(e => e.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(100)
            .IsRequired();
        
        builder.Property(e => e.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(100)
            .IsRequired();
        
        builder.Property(e => e.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(e => e.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(255)
            .IsRequired();
        
        builder.Property(e => e.Phone)
            .HasColumnName("phone")
            .HasMaxLength(20);
        
        builder.Property(e => e.PermitNumber)
            .HasColumnName("permit_number")
            .HasMaxLength(50);
        
        builder.Property(e => e.RoleId)
            .HasColumnName("role_id")
            .IsRequired();
        
        builder.Property(e => e.CompanyId)
            .HasColumnName("company_id")
            .IsRequired();
        
        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(20)
            .HasDefaultValue("active");
        
        builder.Property(e => e.LastLoginAt)
            .HasColumnName("last_login_at");
        
        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");
        
        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // Ignore computed and legacy compatibility properties (not stored in DB)
        builder.Ignore(e => e.FullName);
        builder.Ignore(e => e.IsCompanyAdmin);
        builder.Ignore(e => e.IsSystemAdmin);
        builder.Ignore(e => e.IsAnyAdmin);
        builder.Ignore(e => e.Name);
        builder.Ignore(e => e.Roles);
        builder.Ignore(e => e.Permissions);
        builder.Ignore(e => e.AssignedVehicleIds);
        builder.Ignore(e => e.UserType);
        builder.Ignore(e => e.CIN);
        builder.Ignore(e => e.DateOfBirth);
        builder.Ignore("_rolesCache");
        builder.Ignore("_permissionsCache");
        builder.Ignore("_assignedVehicleIdsCache");
        builder.Ignore("_userTypeCache");

        // Indexes
        builder.HasIndex(e => e.Email)
            .IsUnique()
            .HasDatabaseName("idx_users_email");
        
        builder.HasIndex(e => e.CompanyId)
            .HasDatabaseName("idx_users_company_id");
        
        builder.HasIndex(e => e.RoleId)
            .HasDatabaseName("idx_users_role_id");
        
        builder.HasIndex(e => e.Status)
            .HasDatabaseName("idx_users_status");

        // Relationships
        builder.HasOne(e => e.Societe)
            .WithMany(c => c.Users)
            .HasForeignKey(e => e.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.UserVehicles)
            .WithOne(uv => uv.User)
            .HasForeignKey(uv => uv.UserId)
            .OnDelete(DeleteBehavior.Cascade);
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


