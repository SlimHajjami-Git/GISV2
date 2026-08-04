using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class SubscriptionOrderConfiguration : IEntityTypeConfiguration<SubscriptionOrder>
{
    public void Configure(EntityTypeBuilder<SubscriptionOrder> builder)
    {
        builder.ToTable("subscription_orders");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.SubscriptionTypeId).HasColumnName("subscription_type_id");
        builder.Property(e => e.BillingCycle).HasColumnName("billing_cycle").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending");
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.ProcessedByUserId).HasColumnName("processed_by_user_id");
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // La liste admin trie « en attente d'abord, plus récentes en tête ».
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
        builder.HasIndex(e => e.CompanyId);

        builder.HasOne(e => e.SubscriptionType)
            .WithMany()
            .HasForeignKey(e => e.SubscriptionTypeId);
    }
}
