using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50);
        builder.Property(e => e.Brand).HasColumnName("brand").HasMaxLength(50);
        builder.Property(e => e.Model).HasColumnName("model").HasMaxLength(50);
        builder.Property(e => e.Plate).HasColumnName("plate_number").HasMaxLength(20);
        builder.Property(e => e.Year).HasColumnName("year");
        builder.Property(e => e.Color).HasColumnName("color").HasMaxLength(30);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.HasGps).HasColumnName("has_gps");
        builder.Property(e => e.Mileage).HasColumnName("mileage");
        builder.Property(e => e.RentalMileage).HasColumnName("rental_mileage");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.AssignedDriverId).HasColumnName("assigned_driver_id");
        builder.Property(e => e.AssignedSupervisorId).HasColumnName("assigned_supervisor_id");
        builder.Property(e => e.GpsDeviceId).HasColumnName("gps_device_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.DriverName).HasColumnName("driver_name");
        builder.Property(e => e.DriverPhone).HasColumnName("driver_phone");

        // Acquisition info (created via manual SQL migration 025 as snake_case)
        builder.Property(e => e.AcquisitionType).HasColumnName("acquisition_type").HasMaxLength(20);
        builder.Property(e => e.PurchasePrice).HasColumnName("purchase_price").HasColumnType("decimal(12,2)");
        builder.Property(e => e.LeasingMonthlyPayment).HasColumnName("leasing_monthly_payment").HasColumnType("decimal(10,2)");
        builder.Property(e => e.LeasingDurationMonths).HasColumnName("leasing_duration_months");
        builder.Property(e => e.LeasingStartDate).HasColumnName("leasing_start_date");
        builder.Property(e => e.LeasingPaymentDay).HasColumnName("leasing_payment_day");
        builder.Property(e => e.RegistrationDate).HasColumnName("registration_date");

        // Document start dates (snake_case from migration 025)
        builder.Property(e => e.InsuranceStartDate).HasColumnName("insurance_start_date");
        builder.Property(e => e.TaxStartDate).HasColumnName("tax_start_date");
        builder.Property(e => e.TechnicalInspectionStartDate).HasColumnName("technical_inspection_start_date");

        // Reminder days (snake_case from migration 025)
        builder.Property(e => e.InsuranceReminderDays).HasColumnName("insurance_reminder_days");
        builder.Property(e => e.TaxReminderDays).HasColumnName("tax_reminder_days");
        builder.Property(e => e.TechnicalInspectionReminderDays).HasColumnName("technical_inspection_reminder_days");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.Plate).HasDatabaseName("idx_vehicles_plate_number");

        builder.HasOne(e => e.Societe)
            .WithMany(c => c.Vehicles)
            .HasForeignKey(e => e.CompanyId);

        builder.HasOne(e => e.AssignedDriver)
            .WithMany()
            .HasForeignKey(e => e.AssignedDriverId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.AssignedSupervisor)
            .WithMany()
            .HasForeignKey(e => e.AssignedSupervisorId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.GpsDevice)
            .WithOne(g => g.Vehicle)
            .HasForeignKey<Vehicle>(e => e.GpsDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}


