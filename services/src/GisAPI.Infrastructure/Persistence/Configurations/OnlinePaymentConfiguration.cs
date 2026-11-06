using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

public class OnlinePaymentConfiguration : IEntityTypeConfiguration<OnlinePayment>
{
    public void Configure(EntityTypeBuilder<OnlinePayment> builder)
    {
        builder.ToTable("online_payments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.SubscriptionOrderId).HasColumnName("subscription_order_id");
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired();
        builder.Property(e => e.OrderNumber).HasColumnName("order_number").HasMaxLength(32).IsRequired();
        builder.Property(e => e.ProviderOrderId).HasColumnName("provider_order_id").HasMaxLength(64);
        builder.Property(e => e.AmountMinor).HasColumnName("amount_minor");
        builder.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(e => e.ProviderOrderStatus).HasColumnName("provider_order_status");
        builder.Property(e => e.ActionCode).HasColumnName("action_code");
        builder.Property(e => e.ActionDescription).HasColumnName("action_description").HasMaxLength(512);
        builder.Property(e => e.CardPanMasked).HasColumnName("card_pan_masked").HasMaxLength(32);
        builder.Property(e => e.ApprovalCode).HasColumnName("approval_code").HasMaxLength(16);
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(512);
        builder.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(e => e.PaidAt).HasColumnName("paid_at");
        builder.Property(e => e.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(e => e.OrderNumber).IsUnique();
        builder.HasOne(e => e.SubscriptionOrder).WithMany().HasForeignKey(e => e.SubscriptionOrderId).IsRequired(false);
    }
}
