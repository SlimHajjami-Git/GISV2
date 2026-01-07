using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class MaintenanceRecordConfiguration : IEntityTypeConfiguration<MaintenanceRecord>
{
    public void Configure(EntityTypeBuilder<MaintenanceRecord> builder)
    {
        builder.ToTable("maintenance_records");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(e => e.MileageAtService).HasColumnName("mileage_at_service");
        builder.Property(e => e.Date).HasColumnName("date");
        builder.Property(e => e.NextServiceDate).HasColumnName("next_service_date");
        builder.Property(e => e.NextServiceMileage).HasColumnName("next_service_mileage");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(30);
        builder.Property(e => e.LaborCost).HasColumnName("labor_cost").HasColumnType("decimal(10,2)");
        builder.Property(e => e.PartsCost).HasColumnName("parts_cost").HasColumnType("decimal(10,2)");
        builder.Property(e => e.TotalCost).HasColumnName("total_cost").HasColumnType("decimal(10,2)");
        builder.Property(e => e.ServiceProvider).HasColumnName("service_provider").HasMaxLength(200);
        builder.Property(e => e.ProviderContact).HasColumnName("provider_contact").HasMaxLength(100);
        builder.Property(e => e.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(100);
        builder.Property(e => e.InvoiceUrl).HasColumnName("invoice_url").HasMaxLength(500);
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.VehicleId);

        builder.HasOne(e => e.Vehicle)
            .WithMany(v => v.MaintenanceRecords)
            .HasForeignKey(e => e.VehicleId);
    }
}

public class MaintenancePartConfiguration : IEntityTypeConfiguration<MaintenancePart>
{
    public void Configure(EntityTypeBuilder<MaintenancePart> builder)
    {
        builder.ToTable("maintenance_parts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.MaintenanceRecordId).HasColumnName("maintenance_record_id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.PartNumber).HasColumnName("part_number").HasMaxLength(100);
        builder.Property(e => e.Quantity).HasColumnName("quantity");
        builder.Property(e => e.UnitCost).HasColumnName("unit_cost").HasColumnType("decimal(10,2)");
        builder.Property(e => e.TotalCost).HasColumnName("total_cost").HasColumnType("decimal(10,2)");

        builder.HasOne(e => e.MaintenanceRecord)
            .WithMany(m => m.Parts)
            .HasForeignKey(e => e.MaintenanceRecordId);
    }
}
