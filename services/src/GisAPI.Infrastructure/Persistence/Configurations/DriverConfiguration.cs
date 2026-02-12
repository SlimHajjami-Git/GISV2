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

        builder.Property(d => d.UserId).HasColumnName("user_id");
        builder.Property(d => d.PermitNumber).HasColumnName("permit_number").HasMaxLength(50);
        builder.Property(d => d.PermitType).HasColumnName("permit_type").HasMaxLength(10);
        builder.Property(d => d.PermitExpiry).HasColumnName("permit_expiry");
        builder.Property(d => d.CIN).HasColumnName("cin").HasMaxLength(20);
        builder.Property(d => d.DateOfBirth).HasColumnName("date_of_birth");
        builder.Property(d => d.HireDate).HasColumnName("hire_date");
        builder.Property(d => d.AssignedVehicleId).HasColumnName("assigned_vehicle_id");
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("active");

        builder.HasOne(d => d.User)
            .WithOne()
            .HasForeignKey<Driver>(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.AssignedVehicle)
            .WithMany()
            .HasForeignKey(d => d.AssignedVehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => new { d.CompanyId, d.UserId }).IsUnique();
    }
}
