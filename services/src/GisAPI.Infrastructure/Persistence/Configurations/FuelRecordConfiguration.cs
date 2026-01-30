using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class FuelRecordConfiguration : IEntityTypeConfiguration<FuelRecord>
{
    public void Configure(EntityTypeBuilder<FuelRecord> builder)
    {
        builder.ToTable("fuel_records");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.DriverId).HasColumnName("driver_id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.RecordedAt).HasColumnName("recorded_at");
        builder.Property(e => e.FuelPercent).HasColumnName("fuel_percent");
        builder.Property(e => e.FuelLiters).HasColumnName("fuel_liters");
        builder.Property(e => e.TankCapacityLiters).HasColumnName("tank_capacity_liters");
        builder.Property(e => e.ConsumptionRateLPer100Km).HasColumnName("consumption_rate_l_per_100km");
        builder.Property(e => e.AverageConsumptionLPer100Km).HasColumnName("average_consumption_l_per_100km");
        builder.Property(e => e.OdometerKm).HasColumnName("odometer_km");
        builder.Property(e => e.SpeedKph).HasColumnName("speed_kph");
        builder.Property(e => e.Rpm).HasColumnName("rpm");
        builder.Property(e => e.IgnitionOn).HasColumnName("ignition_on");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50);
        builder.Property(e => e.FuelChange).HasColumnName("fuel_change");
        builder.Property(e => e.RefuelAmount).HasColumnName("refuel_amount");
        builder.Property(e => e.RefuelCost).HasColumnName("refuel_cost");
        builder.Property(e => e.RefuelStation).HasColumnName("refuel_station").HasMaxLength(200);
        builder.Property(e => e.IsAnomaly).HasColumnName("is_anomaly");
        builder.Property(e => e.AnomalyReason).HasColumnName("anomaly_reason").HasMaxLength(500);
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.VehicleId, e.RecordedAt });
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId);

        builder.HasOne(e => e.Driver)
            .WithMany()
            .HasForeignKey(e => e.DriverId);

        builder.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId);
    }
}


