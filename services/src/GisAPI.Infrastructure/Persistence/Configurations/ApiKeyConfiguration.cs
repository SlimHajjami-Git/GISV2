using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GisAPI.Infrastructure.Persistence.Configurations;

/// <summary>Clés d'automatisation de l'API (migration 069).</summary>
public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CompanyId).HasColumnName("company_id");
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.Label).HasColumnName("label").HasMaxLength(100).IsRequired();
        builder.Property(e => e.KeyPrefix).HasColumnName("key_prefix").HasMaxLength(16).IsRequired();
        builder.Property(e => e.KeyHash).HasColumnName("key_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(e => e.LastUsedIp).HasColumnName("last_used_ip").HasMaxLength(64);
        builder.Property(e => e.CreatedIp).HasColumnName("created_ip").HasMaxLength(64);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(e => e.KeyHash).IsUnique();
    }
}
