using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Table acquisition_payments (migration 044) — snake_case, comme
/// vehicle_load_periods. Le schéma réel vit dans migrations/044 ; ce mapping
/// doit lui rester strictement identique (une colonne mappée absente en base
/// = 42703 sur toute lecture).
/// </summary>
public class AcquisitionPaymentConfiguration : IEntityTypeConfiguration<AcquisitionPayment>
{
    public void Configure(EntityTypeBuilder<AcquisitionPayment> builder)
    {
        builder.ToTable("acquisition_payments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.VehicleId).HasColumnName("vehicle_id");
        builder.Property(e => e.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
        builder.Property(e => e.Seq).HasColumnName("seq");
        builder.Property(e => e.DueDate).HasColumnName("due_date");
        builder.Property(e => e.Amount).HasColumnName("amount").HasPrecision(12, 2);
        builder.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired()
            .HasDefaultValue(AcquisitionPayment.Statuses.Planned);
        builder.Property(e => e.PaidAt).HasColumnName("paid_at");
        builder.Property(e => e.PaidAmount).HasColumnName("paid_amount").HasPrecision(12, 2);
        builder.Property(e => e.ReceiptUrl).HasColumnName("receipt_url").HasMaxLength(500);
        builder.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
        // Pas de HasDefaultValue(true) côté EF : sur un bool non nullable, EF
        // prend `false` pour « non renseigné » et laisserait la base poser son
        // DEFAULT true — impossible d'insérer false. La colonne garde son
        // DEFAULT en SQL (044) ; l'entité envoie toujours la valeur explicitement.
        builder.Property(e => e.Generated).HasColumnName("generated");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.VehicleId, e.Kind, e.Seq })
            .IsUnique()
            .HasDatabaseName("ux_acquisition_payments_vehicle_kind_seq");
        builder.HasIndex(e => new { e.CompanyId, e.DueDate })
            .HasDatabaseName("idx_acquisition_payments_company_due");
        builder.HasIndex(e => e.VehicleId)
            .HasDatabaseName("idx_acquisition_payments_vehicle");

        builder.HasOne(e => e.Vehicle)
            .WithMany()
            .HasForeignKey(e => e.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
