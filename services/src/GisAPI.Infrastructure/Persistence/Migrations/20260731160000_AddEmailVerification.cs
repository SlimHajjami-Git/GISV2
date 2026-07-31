using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Confirmation d'adresse email à l'inscription libre.
///
/// Deux colonnes sur users plutôt qu'une table dédiée : il n'existe jamais qu'un
/// seul jeton valide par compte, et un renvoi de l'email doit invalider le
/// précédent — l'écrasement d'une colonne le fait sans code de nettoyage.
///
/// Index partiel sur le jeton : la table users est majoritairement composée de
/// comptes actifs sans jeton, il est inutile de les indexer.
///
/// Idempotente (IF NOT EXISTS) — sans risque en production.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260731160000_AddEmailVerification")]
public partial class AddEmailVerification : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verification_token varchar(128);
ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verification_expires_at timestamptz;
CREATE INDEX IF NOT EXISTS idx_users_email_verification_token
    ON users (email_verification_token)
    WHERE email_verification_token IS NOT NULL;
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DROP INDEX IF EXISTS idx_users_email_verification_token;
ALTER TABLE users DROP COLUMN IF EXISTS email_verification_expires_at;
ALTER TABLE users DROP COLUMN IF EXISTS email_verification_token;
");
    }
}
