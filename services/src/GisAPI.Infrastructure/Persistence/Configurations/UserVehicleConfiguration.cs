using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class UserVehicleConfiguration : IEntityTypeConfiguration<UserVehicle>
{
    public void Configure(EntityTypeBuilder<UserVehicle> builder)
    {
        builder.ToTable("user_vehicles");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(e => e.VehicleId)
            .HasColumnName("vehicle_id")
            .IsRequired();

        builder.Property(e => e.AssignedAt)
            .HasColumnName("assigned_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.AssignedById)
            .HasColumnName("assigned_by");

        // Unique constraint: user can only be assigned to a vehicle once
        builder.HasIndex(e => new { e.UserId, e.VehicleId })
            .IsUnique()
            .HasDatabaseName("uq_user_vehicle");

        builder.HasIndex(e => e.UserId)
            .HasDatabaseName("idx_user_vehicles_user_id");

        builder.HasIndex(e => e.VehicleId)
            .HasDatabaseName("idx_user_vehicles_vehicle_id");

        // Relationships
        builder.HasOne(e => e.User)
            .WithMany(u => u.UserVehicles)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.AssignedBy)
            .WithMany()
            .HasForeignKey(e => e.AssignedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}


