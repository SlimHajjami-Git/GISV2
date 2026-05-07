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

        // Origin & identification
        builder.Property(e => e.Origin).HasColumnName("origin").HasMaxLength(20).IsRequired().HasDefaultValue("auto");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.GpsDeviceId).HasColumnName("gps_device_id");
        builder.Property(e => e.DriverId).HasColumnName("driver_id");
        builder.Property(e => e.DeviceUid).HasColumnName("device_uid").HasMaxLength(50).IsRequired();
        builder.Property(e => e.IncidentAt).HasColumnName("incident_at");
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.ReferenceCode).HasColumnName("reference_code").HasMaxLength(100);
        builder.Property(e => e.VehicleLabel).HasColumnName("vehicle_label").HasMaxLength(100);
        builder.Property(e => e.LocationCommune).HasColumnName("location_commune").HasMaxLength(100);
        builder.Property(e => e.LocationGovernorate).HasColumnName("location_governorate").HasMaxLength(100);
        builder.Property(e => e.LocationRoadType).HasColumnName("location_road_type").HasMaxLength(100);

        // Phase 1 — Detection / sensor
        builder.Property(e => e.SynthesisText).HasColumnName("synthesis_text");
        builder.Property(e => e.Confidence).HasColumnName("confidence");
        builder.Property(e => e.StoryJson).HasColumnName("story").HasColumnType("jsonb");
        builder.Property(e => e.ReasonsJson).HasColumnName("reasons").HasColumnType("jsonb");
        builder.Property(e => e.IndicatorsJson).HasColumnName("indicators").HasColumnType("jsonb");
        builder.Property(e => e.WeatherConditions).HasColumnName("weather_conditions").HasMaxLength(100);
        builder.Property(e => e.RoadConditions).HasColumnName("road_conditions").HasMaxLength(100);
        builder.Property(e => e.PoliceReportNumber).HasColumnName("police_report_number").HasMaxLength(100);
        builder.Property(e => e.MileageAtAccident).HasColumnName("mileage_at_accident");

        // Phase 2 — Confirmation
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(30).IsRequired().HasDefaultValue("pending");
        builder.Property(e => e.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(e => e.DecidedAt).HasColumnName("decided_at");
        builder.Property(e => e.InitialDescription).HasColumnName("initial_description");
        builder.Property(e => e.InitialSeverity).HasColumnName("initial_severity").HasMaxLength(20);
        builder.Property(e => e.DamagedZonesJson).HasColumnName("damaged_zones_json").HasColumnType("jsonb");

        // Phase 3 — Expert assessment
        builder.Property(e => e.ExpertVisitedAt).HasColumnName("expert_visited_at");
        builder.Property(e => e.ExpertName).HasColumnName("expert_name").HasMaxLength(200);
        builder.Property(e => e.ExpertCompany).HasColumnName("expert_company").HasMaxLength(200);
        builder.Property(e => e.ExpertAssessment).HasColumnName("expert_assessment");
        builder.Property(e => e.ExpertEstimatedAmount).HasColumnName("expert_estimated_amount").HasColumnType("numeric(12,2)");

        // Phase 4 — Mechanic quote
        builder.Property(e => e.MechanicQuoteAt).HasColumnName("mechanic_quote_at");
        builder.Property(e => e.MechanicName).HasColumnName("mechanic_name").HasMaxLength(200);
        builder.Property(e => e.MechanicQuotedAmount).HasColumnName("mechanic_quoted_amount").HasColumnType("numeric(12,2)");

        // Phase 5 — Repair
        builder.Property(e => e.RepairStartedAt).HasColumnName("repair_started_at");
        builder.Property(e => e.RepairCompletedAt).HasColumnName("repair_completed_at");
        builder.Property(e => e.ActualRepairCost).HasColumnName("actual_repair_cost").HasColumnType("numeric(12,2)");
        builder.Property(e => e.TowDetectedAt).HasColumnName("tow_detected_at");

        // Phase 6 — Insurance settlement
        builder.Property(e => e.ClaimNumber).HasColumnName("claim_number").HasMaxLength(100);
        builder.Property(e => e.ClaimSubmittedAt).HasColumnName("claim_submitted_at");
        builder.Property(e => e.ClaimApprovedAmount).HasColumnName("claim_approved_amount").HasColumnType("numeric(12,2)");
        builder.Property(e => e.ClaimStatus).HasColumnName("claim_status").HasMaxLength(30);
        builder.Property(e => e.ThirdPartyInvolved).HasColumnName("third_party_involved").HasDefaultValue(false);

        // Misc
        builder.Property(e => e.Witnesses).HasColumnName("witnesses");
        builder.Property(e => e.AdditionalNotes).HasColumnName("additional_notes");
        builder.Property(e => e.PdfReportUrl).HasColumnName("pdf_report_url").HasMaxLength(500);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.VehicleId);
        builder.HasIndex(e => e.IncidentAt).IsDescending();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.Origin);
        builder.HasIndex(e => e.ClaimStatus);

        builder.HasMany(e => e.Documents)
            .WithOne(d => d.AccidentEvent)
            .HasForeignKey(d => d.AccidentEventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.ThirdParties)
            .WithOne(t => t.AccidentEvent)
            .HasForeignKey(t => t.AccidentEventId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AccidentEventDocumentConfiguration : IEntityTypeConfiguration<AccidentEventDocument>
{
    public void Configure(EntityTypeBuilder<AccidentEventDocument> builder)
    {
        builder.ToTable("accident_event_documents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AccidentEventId).HasColumnName("accident_event_id");
        builder.Property(e => e.DocumentType).HasColumnName("document_type").HasMaxLength(50).IsRequired().HasDefaultValue("other");
        builder.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(300).IsRequired();
        builder.Property(e => e.FileUrl).HasColumnName("file_url").HasMaxLength(500).IsRequired();
        builder.Property(e => e.FileSize).HasColumnName("file_size");
        builder.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(100);
        builder.Property(e => e.UploadedByUserId).HasColumnName("uploaded_by_user_id");
        builder.Property(e => e.UploadedAt).HasColumnName("uploaded_at");
        builder.HasIndex(e => e.AccidentEventId);
        builder.HasIndex(e => e.DocumentType);
    }
}

public class AccidentEventThirdPartyConfiguration : IEntityTypeConfiguration<AccidentEventThirdParty>
{
    public void Configure(EntityTypeBuilder<AccidentEventThirdParty> builder)
    {
        builder.ToTable("accident_event_third_parties");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.AccidentEventId).HasColumnName("accident_event_id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
        builder.Property(e => e.VehiclePlate).HasColumnName("vehicle_plate").HasMaxLength(50);
        builder.Property(e => e.VehicleModel).HasColumnName("vehicle_model").HasMaxLength(100);
        builder.Property(e => e.InsuranceCompany).HasColumnName("insurance_company").HasMaxLength(200);
        builder.Property(e => e.InsuranceNumber).HasColumnName("insurance_number").HasMaxLength(100);
        builder.Property(e => e.InsuranceExpiry).HasColumnName("insurance_expiry");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(e => e.AccidentEventId);
    }
}
