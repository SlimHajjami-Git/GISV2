using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class VehicleCostConfiguration : IEntityTypeConfiguration<VehicleCost>
{
    public void Configure(EntityTypeBuilder<VehicleCost> builder)
    {
        builder.ToTable("vehicle_costs");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)");
        builder.Property(e => e.Date).HasColumnName("date");
        builder.Property(e => e.Mileage).HasColumnName("mileage");
        builder.Property(e => e.ReceiptNumber).HasColumnName("receipt_number").HasMaxLength(100);
        builder.Property(e => e.ReceiptUrl).HasColumnName("receipt_url").HasMaxLength(500);
        builder.Property(e => e.FuelType).HasColumnName("fuel_type").HasMaxLength(20);
        builder.Property(e => e.Liters).HasColumnName("liters").HasColumnType("decimal(10,2)");
        builder.Property(e => e.PricePerLiter).HasColumnName("price_per_liter").HasColumnType("decimal(10,2)");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.VehicleId);

        builder.HasOne(e => e.Vehicle)
            .WithMany(v => v.Costs)
            .HasForeignKey(e => e.VehicleId);
    }
}


