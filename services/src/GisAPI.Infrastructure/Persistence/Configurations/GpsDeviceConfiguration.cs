using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class GpsDeviceConfiguration : IEntityTypeConfiguration<GpsDevice>
{
    public void Configure(EntityTypeBuilder<GpsDevice> builder)
    {
        builder.ToTable("gps_devices");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceUid).HasColumnName("device_uid").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Mat).HasColumnName("mat").HasMaxLength(50);
        builder.Property(e => e.Label).HasColumnName("label").HasMaxLength(100);
        builder.Property(e => e.SimNumber).HasColumnName("sim_number").HasMaxLength(20);
        builder.Property(e => e.SimOperator).HasColumnName("sim_operator").HasMaxLength(50);
        builder.Property(e => e.Model).HasColumnName("model").HasMaxLength(50);
        builder.Property(e => e.Brand).HasColumnName("brand").HasMaxLength(50);
        builder.Property(e => e.ProtocolType).HasColumnName("protocol_type").HasMaxLength(50);
        builder.Property(e => e.FirmwareVersion).HasColumnName("firmware_version").HasMaxLength(50);
        builder.Property(e => e.InstallationDate).HasColumnName("installation_date");
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
        builder.Property(e => e.LastCommunication).HasColumnName("last_communication");
        builder.Property(e => e.BatteryLevel).HasColumnName("battery_level");
        builder.Property(e => e.SignalStrength).HasColumnName("signal_strength");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.DeviceUid).IsUnique();
        builder.HasIndex(e => e.Mat);
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Company)
            .WithMany(c => c.GpsDevices)
            .HasForeignKey(e => e.CompanyId);
    }
}

public class GpsPositionConfiguration : IEntityTypeConfiguration<GpsPosition>
{
    public void Configure(EntityTypeBuilder<GpsPosition> builder)
    {
        builder.ToTable("gps_positions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.RecordedAt).HasColumnName("recorded_at");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.SpeedKph).HasColumnName("speed_kph");
        builder.Property(e => e.CourseDeg).HasColumnName("course_deg");
        builder.Property(e => e.AltitudeM).HasColumnName("altitude_m");
        builder.Property(e => e.IgnitionOn).HasColumnName("ignition_on");
        builder.Property(e => e.FuelRaw).HasColumnName("fuel_raw");
        builder.Property(e => e.PowerVoltage).HasColumnName("power_voltage");
        builder.Property(e => e.Satellites).HasColumnName("satellites");
        builder.Property(e => e.IsValid).HasColumnName("is_valid");
        builder.Property(e => e.IsRealTime).HasColumnName("is_real_time");
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(500);
        builder.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        // MEMS (Accelerometer) columns
        builder.Property(e => e.MemsX).HasColumnName("mems_x");
        builder.Property(e => e.MemsY).HasColumnName("mems_y");
        builder.Property(e => e.MemsZ).HasColumnName("mems_z");

        // FMS (Fleet Management System) columns
        builder.Property(e => e.TemperatureC).HasColumnName("temperature_c");
        builder.Property(e => e.OdometerKm).HasColumnName("odometer_km");
        builder.Property(e => e.Rpm).HasColumnName("rpm");
        builder.Property(e => e.FuelRateLPer100Km).HasColumnName("fuel_rate_l_per_100km");

        // Protocol info columns
        builder.Property(e => e.SendFlag).HasColumnName("send_flag");
        builder.Property(e => e.ProtocolVersion).HasColumnName("protocol_version");

        builder.HasIndex(e => e.DeviceId);
        builder.HasIndex(e => e.RecordedAt).IsDescending();

        builder.HasOne(e => e.Device)
            .WithMany(d => d.Positions)
            .HasForeignKey(e => e.DeviceId);
    }
}

public class GpsAlertConfiguration : IEntityTypeConfiguration<GpsAlert>
{
    public void Configure(EntityTypeBuilder<GpsAlert> builder)
    {
        builder.ToTable("gps_alerts");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(50);
        builder.Property(e => e.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(e => e.Resolved).HasColumnName("resolved");
        builder.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.Timestamp).HasColumnName("timestamp");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(e => e.Timestamp).IsDescending();

        builder.HasOne(e => e.Device)
            .WithMany(d => d.Alerts)
            .HasForeignKey(e => e.DeviceId);
    }
}
