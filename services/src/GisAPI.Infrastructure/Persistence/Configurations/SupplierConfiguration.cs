using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("Id");
        builder.Property(e => e.CompanyId).HasColumnName("CompanyId");
        builder.Property(e => e.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Type).HasColumnName("Type").HasMaxLength(50).HasDefaultValue("general");
        builder.Property(e => e.Address).HasColumnName("Address").HasMaxLength(500);
        builder.Property(e => e.City).HasColumnName("City").HasMaxLength(100);
        builder.Property(e => e.ContactName).HasColumnName("ContactName").HasMaxLength(200);
        builder.Property(e => e.Phone).HasColumnName("Phone").HasMaxLength(50);
        builder.Property(e => e.Email).HasColumnName("Email").HasMaxLength(200);
        builder.Property(e => e.Website).HasColumnName("Website").HasMaxLength(500);
        builder.Property(e => e.TaxId).HasColumnName("TaxId").HasMaxLength(50);
        builder.Property(e => e.BankAccount).HasColumnName("BankAccount").HasMaxLength(100);
        builder.Property(e => e.PaymentTerms).HasColumnName("PaymentTerms").HasMaxLength(50).HasDefaultValue("net30");
        builder.Property(e => e.DiscountPercent).HasColumnName("DiscountPercent").HasPrecision(5, 2);
        builder.Property(e => e.Rating).HasColumnName("Rating").HasPrecision(3, 1).HasDefaultValue(0);
        builder.Property(e => e.Notes).HasColumnName("Notes");
        builder.Property(e => e.IsActive).HasColumnName("IsActive").HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
        builder.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.Name);

        // Ignore properties that don't exist in database
        builder.Ignore(e => e.PostalCode);
        builder.Ignore(e => e.MaintenanceRecords);
        builder.Ignore(e => e.Services);
    }
}

public class SupplierServiceConfiguration : IEntityTypeConfiguration<SupplierService>
{
    public void Configure(EntityTypeBuilder<SupplierService> builder)
    {
        builder.ToTable("supplier_services");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.SupplierId).HasColumnName("supplier_id");
        builder.Property(e => e.ServiceCode).HasColumnName("service_code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasOne(e => e.Supplier)
            .WithMany(s => s.Services)
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SupplierId);
    }
}

public class PartInventoryConfiguration : IEntityTypeConfiguration<PartInventory>
{
    public void Configure(EntityTypeBuilder<PartInventory> builder)
    {
        builder.ToTable("part_inventory");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.SupplierId).HasColumnName("supplier_id");
        builder.Property(e => e.PartNumber).HasColumnName("part_number").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).HasDefaultValue("general");
        builder.Property(e => e.Brand).HasColumnName("brand").HasMaxLength(100);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(20);
        builder.Property(e => e.QuantityInStock).HasColumnName("quantity_in_stock").HasDefaultValue(0);
        builder.Property(e => e.MinimumStock).HasColumnName("minimum_stock").HasDefaultValue(0);
        builder.Property(e => e.ReorderQuantity).HasColumnName("reorder_quantity");
        builder.Property(e => e.UnitCost).HasColumnName("unit_cost").HasPrecision(10, 2);
        builder.Property(e => e.SellingPrice).HasColumnName("selling_price").HasPrecision(10, 2);
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(100);
        builder.Property(e => e.CompatibleVehicles).HasColumnName("compatible_vehicles").HasColumnType("text[]");
        builder.Property(e => e.LastRestockDate).HasColumnName("last_restock_date");
        builder.Property(e => e.ExpiryDate).HasColumnName("expiry_date");
        builder.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(e => e.Supplier)
            .WithMany(s => s.Parts)
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.PartNumber);
    }
}

public class PartTransactionConfiguration : IEntityTypeConfiguration<PartTransaction>
{
    public void Configure(EntityTypeBuilder<PartTransaction> builder)
    {
        builder.ToTable("part_transactions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.PartId).HasColumnName("part_id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Quantity).HasColumnName("quantity");
        builder.Property(e => e.QuantityBefore).HasColumnName("quantity_before");
        builder.Property(e => e.QuantityAfter).HasColumnName("quantity_after");
        builder.Property(e => e.UnitCost).HasColumnName("unit_cost").HasPrecision(10, 2);
        builder.Property(e => e.TotalCost).HasColumnName("total_cost").HasPrecision(10, 2);
        builder.Property(e => e.MaintenanceRecordId).HasColumnName("maintenance_record_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.SupplierId).HasColumnName("supplier_id");
        builder.Property(e => e.ReferenceNumber).HasColumnName("reference_number").HasMaxLength(100);
        builder.Property(e => e.Notes).HasColumnName("notes");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasOne(e => e.Part)
            .WithMany()
            .HasForeignKey(e => e.PartId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.MaintenanceRecord)
            .WithMany()
            .HasForeignKey(e => e.MaintenanceRecordId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.Supplier)
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.PartId);
    }
}
