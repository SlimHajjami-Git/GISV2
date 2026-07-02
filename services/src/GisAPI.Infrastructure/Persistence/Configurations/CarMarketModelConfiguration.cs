using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class CarMarketModelConfiguration : IEntityTypeConfiguration<CarMarketModel>
{
    public void Configure(EntityTypeBuilder<CarMarketModel> builder)
    {
        builder.ToTable("car_market_models");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Brand).HasColumnName("brand").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Model).HasColumnName("model").HasMaxLength(64).IsRequired();
        builder.Property(e => e.Generation).HasColumnName("generation").HasMaxLength(64);

        builder.Property(e => e.YearFrom).HasColumnName("year_from");
        builder.Property(e => e.YearTo).HasColumnName("year_to");

        builder.Property(e => e.Segment).HasColumnName("segment").HasMaxLength(32).IsRequired();
        builder.Property(e => e.Energy).HasColumnName("energy").HasMaxLength(16).IsRequired();
        builder.Property(e => e.EngineLabel).HasColumnName("engine_label").HasMaxLength(64);
        builder.Property(e => e.FiscalPower).HasColumnName("fiscal_power");
        builder.Property(e => e.Seats).HasColumnName("seats");

        builder.Property(e => e.NewPriceTnd).HasColumnName("new_price_tnd").HasPrecision(12, 2);
        builder.Property(e => e.RealConsumptionL100).HasColumnName("real_consumption_l100");
        builder.Property(e => e.ReliabilityScore).HasColumnName("reliability_score");
        builder.Property(e => e.BuyingAdvice).HasColumnName("buying_advice");

        builder.Property(e => e.KnownIssuesJson).HasColumnName("known_issues").HasColumnType("jsonb").HasDefaultValue("[]");
        builder.Property(e => e.PartsPricesJson).HasColumnName("parts_prices").HasColumnType("jsonb").HasDefaultValue("[]");
        builder.Property(e => e.PriceCurveJson).HasColumnName("price_curve").HasColumnType("jsonb").HasDefaultValue("[]");

        builder.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // Lookups are by brand/model text and by budget filters over the whole
        // (small, curated) table — a simple brand+model index is enough.
        builder.HasIndex(e => new { e.Brand, e.Model });
        builder.HasIndex(e => e.Segment);
    }
}
