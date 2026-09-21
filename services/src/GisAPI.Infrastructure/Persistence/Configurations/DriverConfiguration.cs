using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.CompanyId).HasColumnName("company_id");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        // Identity (native on driver; no longer on a linked user).
        builder.Property(d => d.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Email).HasColumnName("email").HasMaxLength(255);
        builder.Property(d => d.Phone).HasColumnName("phone").HasMaxLength(50);

        // Permit + personal info.
        builder.Property(d => d.PermitNumber).HasColumnName("permit_number").HasMaxLength(50);
        builder.Property(d => d.PermitType).HasColumnName("permit_type").HasMaxLength(10);
        builder.Property(d => d.PermitExpiry).HasColumnName("permit_expiry");
        builder.Property(d => d.CIN).HasColumnName("cin").HasMaxLength(20);
        builder.Property(d => d.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(d => d.HireDate).HasColumnName("hire_date");

        builder.Property(d => d.AssignedVehicleId).HasColumnName("assigned_vehicle_id");
        // Migration 050 : compte de connexion du chauffeur, FK users ON DELETE SET NULL,
        // unique partiel (un compte, une fiche). Scalaire sans navigation, comme le véhicule.
        builder.Property(d => d.UserId).HasColumnName("user_id");
        builder.HasIndex(d => d.UserId).IsUnique().HasFilter("user_id IS NOT NULL").HasDatabaseName("ux_drivers_user_id");
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");

        // No navigation property for AssignedVehicle — consumers must join on
        // AssignedVehicleId manually. See Driver.cs for why (would collide with
        // Vehicle.AssignedDriver and create an ambiguous 1-1 relationship).

        // Non-unique index for tenant-scoped lookups + name ordering.
        builder.HasIndex(d => new { d.CompanyId, d.LastName, d.FirstName })
            .HasDatabaseName("ix_drivers_company_id_name");
    }
}
