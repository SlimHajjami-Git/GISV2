using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class PointOfInterestConfiguration : IEntityTypeConfiguration<PointOfInterest>
{
    public void Configure(EntityTypeBuilder<PointOfInterest> builder)
    {
        builder.ToTable("points_of_interest");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(e => e.SubCategory).HasColumnName("sub_category").HasMaxLength(50);
        builder.Property(e => e.Latitude).HasColumnName("latitude");
        builder.Property(e => e.Longitude).HasColumnName("longitude");
        builder.Property(e => e.Radius).HasColumnName("radius");
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(30);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(e => e.Website).HasColumnName("website").HasMaxLength(200);
        builder.Property(e => e.ContactName).HasColumnName("contact_name").HasMaxLength(100);
        builder.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(100);
        // Hours is configured as jsonb in GisDbContext.cs
        builder.Property(e => e.Color).HasColumnName("color").HasMaxLength(20);
        builder.Property(e => e.Icon).HasColumnName("icon").HasMaxLength(50);
        builder.Property(e => e.AlertOnArrival).HasColumnName("alert_on_arrival");
        builder.Property(e => e.AlertOnDeparture).HasColumnName("alert_on_departure");
        builder.Property(e => e.ExpectedStayMinutes).HasColumnName("expected_stay_minutes");
        builder.Property(e => e.NotificationCooldownMinutes).HasColumnName("notification_cooldown_minutes");
        builder.Property(e => e.Tags).HasColumnName("tags").HasColumnType("jsonb");
        builder.Property(e => e.IsActive).HasColumnName("is_active");
        builder.Property(e => e.VisitCount).HasColumnName("visit_count");
        builder.Property(e => e.LastVisitAt).HasColumnName("last_visit_at");
        builder.Property(e => e.FuelBrand).HasColumnName("fuel_brand").HasMaxLength(50);
        builder.Property(e => e.HasDiesel).HasColumnName("has_diesel");
        builder.Property(e => e.HasGasoline).HasColumnName("has_gasoline");
        builder.Property(e => e.HasElectricCharging).HasColumnName("has_electric_charging");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CompanyId);
        builder.HasIndex(e => e.Category);

        builder.HasOne(e => e.Societe)
            .WithMany()
            .HasForeignKey(e => e.CompanyId);
    }
}

public class PoiVisitConfiguration : IEntityTypeConfiguration<PoiVisit>
{
    public void Configure(EntityTypeBuilder<PoiVisit> builder)
    {
        builder.ToTable("poi_visits");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.PoiId).HasColumnName("poi_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.DeviceId).HasColumnName("device_id");
        builder.Property(e => e.ArrivalAt).HasColumnName("arrival_at");
        builder.Property(e => e.DepartureAt).HasColumnName("departure_at");
        builder.Property(e => e.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(e => e.ArrivalLat).HasColumnName("arrival_lat");
        builder.Property(e => e.ArrivalLng).HasColumnName("arrival_lng");
        builder.Property(e => e.DepartureLat).HasColumnName("departure_lat");
        builder.Property(e => e.DepartureLng).HasColumnName("departure_lng");
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(500);
        builder.Property(e => e.IsNotified).HasColumnName("is_notified");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");

        builder.HasIndex(e => new { e.PoiId, e.ArrivalAt });
        builder.HasIndex(e => new { e.VehicleId, e.ArrivalAt });
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.Poi)
            .WithMany(p => p.Visits)
            .HasForeignKey(e => e.PoiId);

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId);
    }
}
