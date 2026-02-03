using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class FuelEntryConfiguration : IEntityTypeConfiguration<FuelEntry>
{
    public void Configure(EntityTypeBuilder<FuelEntry> builder)
    {
        builder.ToTable("fuel_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.VehiclePlate).HasColumnName("vehicle_plate").HasMaxLength(20).IsRequired();
        builder.Property(e => e.FuelTypeId).HasColumnName("fuel_type_id");
        builder.Property(e => e.Volume).HasColumnName("volume").HasPrecision(10, 2);
        builder.Property(e => e.PricePerLiter).HasColumnName("price_per_liter").HasPrecision(10, 3);
        builder.Property(e => e.TotalAmount).HasColumnName("total_amount").HasPrecision(12, 2);
        builder.Property(e => e.InvoiceDate).HasColumnName("invoice_date");
        builder.Property(e => e.StationName).HasColumnName("station_name").HasMaxLength(100);
        builder.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(50);
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(e => e.DriverId).HasColumnName("driver_id");
        builder.Property(e => e.OdometerKm).HasColumnName("odometer_km");

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.FuelType)
            .WithMany()
            .HasForeignKey(e => e.FuelTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Driver)
            .WithMany()
            .HasForeignKey(e => e.DriverId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.CompanyId, e.InvoiceDate }).HasDatabaseName("ix_fuel_entries_company_date");
        builder.HasIndex(e => new { e.CompanyId, e.VehiclePlate }).HasDatabaseName("ix_fuel_entries_company_plate");
    }
}
