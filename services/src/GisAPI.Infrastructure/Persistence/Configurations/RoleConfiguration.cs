using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");

        builder.HasKey(r => r.Id);
        
        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description)
            .HasColumnName("description")
            .HasMaxLength(500);

        builder.Property(r => r.SocieteId)
            .HasColumnName("societe_id")
            .IsRequired(false);

        builder.Property(r => r.IsCompanyAdmin)
            .HasColumnName("is_company_admin")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.IsSystemRole)
            .HasColumnName("is_system_role")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Ignore(r => r.IsSystemAdmin);

        builder.Property(r => r.Permissions)
            .HasColumnName("permissions")
            .HasColumnType("jsonb");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.Societe)
            .WithMany(s => s.Roles)
            .HasForeignKey(r => r.SocieteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Users)
            .WithOne(u => u.Role)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index for unique company admin per company
        builder.HasIndex(r => r.SocieteId)
            .HasDatabaseName("idx_roles_societe_id");
    }
}


