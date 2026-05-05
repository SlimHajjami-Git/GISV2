using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

/// <summary>
/// Calypso 7 — la table <c>alert_emails</c> existait depuis Sprint D mais
/// l'entité <see cref="AlertEmail"/> n'avait jamais eu de configuration EF.
/// EF utilisait alors le nom des propriétés tel quel (PascalCase) alors
/// que les colonnes DB sont en snake_case → toute requête sur AlertEmails
/// renvoyait une 500. On corrige ça avec un mapping explicite.
/// </summary>
public class AlertEmailConfiguration : IEntityTypeConfiguration<AlertEmail>
{
    public void Configure(EntityTypeBuilder<AlertEmail> builder)
    {
        builder.ToTable("alert_emails");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CompanyId).HasColumnName("company_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.Property(a => a.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
        builder.Property(a => a.AlertType).HasColumnName("alert_type").HasMaxLength(50).IsRequired();

        builder.HasIndex(a => a.CompanyId).HasDatabaseName("idx_alert_emails_company_id");
        builder.HasIndex(a => a.AlertType).HasDatabaseName("idx_alert_emails_type");
    }
}
