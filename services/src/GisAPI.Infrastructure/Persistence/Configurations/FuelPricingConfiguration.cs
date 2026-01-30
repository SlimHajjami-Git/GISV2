using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class FuelPricingConfiguration : IEntityTypeConfiguration<FuelPricing>
{
    public void Configure(EntityTypeBuilder<FuelPricing> builder)
    {
        builder.ToTable("fuel_pricing");

        builder.HasKey(f => f.Id);

        builder.HasOne(f => f.FuelType)
            .WithMany(ft => ft.Pricings)
            .HasForeignKey(f => f.FuelTypeId);

        // Map Societe navigation to use CompanyId as foreign key
        builder.HasOne(f => f.Societe)
            .WithMany()
            .HasForeignKey(f => f.CompanyId);
    }
}


