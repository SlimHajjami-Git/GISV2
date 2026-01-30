using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GisAPI.Domain.Entities;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class PartPricingConfiguration : IEntityTypeConfiguration<PartPricing>
{
    public void Configure(EntityTypeBuilder<PartPricing> builder)
    {
        builder.ToTable("part_pricing");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Price)
            .HasPrecision(18, 2);
            
        builder.Property(x => x.Supplier)
            .HasMaxLength(200);
            
        builder.Property(x => x.Notes)
            .HasMaxLength(500);
            
        builder.HasOne(x => x.Part)
            .WithMany()
            .HasForeignKey(x => x.PartId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne(x => x.Societe)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasIndex(x => new { x.CompanyId, x.PartId }).IsUnique();
    }
}


