using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class VehicleLoadPeriodConfiguration : IEntityTypeConfiguration<VehicleLoadPeriod>
{
    public void Configure(EntityTypeBuilder<VehicleLoadPeriod> builder)
    {
        builder.ToTable("vehicle_load_periods");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.StartTime).HasColumnName("start_time");
        builder.Property(e => e.EndTime).HasColumnName("end_time");
        builder.Property(e => e.TonnageT).HasColumnName("tonnage_t").HasPrecision(6, 2);
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(300);
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.VehicleId, e.StartTime });
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
