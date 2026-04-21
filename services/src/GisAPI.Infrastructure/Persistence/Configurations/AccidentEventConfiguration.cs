using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class AccidentEventConfiguration : IEntityTypeConfiguration<AccidentEvent>
{
    public void Configure(EntityTypeBuilder<AccidentEvent> builder)
    {
        builder.ToTable("accident_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.GpsDeviceId).HasColumnName("gps_device_id");
        builder.Property(e => e.DeviceUid).HasColumnName("device_uid").HasMaxLength(50).IsRequired();
        builder.Property(e => e.IncidentAt).HasColumnName("incident_at");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.ReferenceCode).HasColumnName("reference_code").HasMaxLength(100);
        builder.Property(e => e.VehicleLabel).HasColumnName("vehicle_label").HasMaxLength(100);
        builder.Property(e => e.LocationCommune).HasColumnName("location_commune").HasMaxLength(100);
        builder.Property(e => e.LocationGovernorate).HasColumnName("location_governorate").HasMaxLength(100);
        builder.Property(e => e.LocationRoadType).HasColumnName("location_road_type").HasMaxLength(100);
        builder.Property(e => e.SynthesisText).HasColumnName("synthesis_text");
        builder.Property(e => e.Confidence).HasColumnName("confidence");
        builder.Property(e => e.StoryJson).HasColumnName("story").HasColumnType("jsonb");
        builder.Property(e => e.ReasonsJson).HasColumnName("reasons").HasColumnType("jsonb");
        builder.Property(e => e.IndicatorsJson).HasColumnName("indicators").HasColumnType("jsonb");

        // Decision workflow — see AccidentEvent.Status / DecidedByUserId / DecidedAt
        // / TowDetectedAt for semantics.
        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue("pending");
        builder.Property(e => e.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(e => e.DecidedAt).HasColumnName("decided_at");
        builder.Property(e => e.TowDetectedAt).HasColumnName("tow_detected_at");

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.VehicleId);
        builder.HasIndex(e => e.IncidentAt).IsDescending();
        builder.HasIndex(e => e.Status);

        // Intentionally no navigation FK constraints — the entity survives
        // if the vehicle / device row is later renamed or detached.
    }
}
