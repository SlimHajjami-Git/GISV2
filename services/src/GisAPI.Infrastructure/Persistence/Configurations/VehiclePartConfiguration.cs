using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GisAPI.Domain.Entities;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class VehiclePartConfiguration : IEntityTypeConfiguration<VehiclePart>
{
    public void Configure(EntityTypeBuilder<VehiclePart> builder)
    {
        builder.ToTable("vehicle_parts");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(x => x.Description)
            .HasMaxLength(500);
            
        builder.Property(x => x.PartNumber)
            .HasMaxLength(50);
            
        builder.HasIndex(x => x.CategoryId);
    }
}


