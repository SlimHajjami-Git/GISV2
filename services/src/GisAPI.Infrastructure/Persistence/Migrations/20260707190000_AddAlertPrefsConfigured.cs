using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute users.alert_prefs_configured : marqueur d'un enregistrement EXPLICITE
/// des préférences d'alertes email. Sans lui, « personne n'a coché » (société
/// jamais configurée → fallback legacy vers les admins) est indistinguable de
/// « tout le monde a décoché » (opt-out volontaire → aucun envoi), et le
/// fallback ré-abonnait de force les admins qui venaient de tout décocher.
/// Migration idempotente (ADD COLUMN IF NOT EXISTS) pour rester sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260707190000_AddAlertPrefsConfigured")]
public partial class AddAlertPrefsConfigured : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS alert_prefs_configured boolean NOT NULL DEFAULT false;");

        // Les utilisateurs qui ont déjà AU MOINS une case cochée ont forcément
        // configuré leurs préférences — on les marque pour que le comportement
        // reste identique pour eux après la mise à jour.
        migrationBuilder.Sql(@"
UPDATE users SET alert_prefs_configured = TRUE
WHERE alert_assurance OR alert_taxe_circulation OR alert_visite_technique OR alert_entretien;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE users DROP COLUMN IF EXISTS alert_prefs_configured;");
    }
}
