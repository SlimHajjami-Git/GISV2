using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute la preference utilisateur "daily_report_email_enabled" : quand elle
/// est activee, l'utilisateur recoit par email le rapport journalier d'activite
/// de toute la flotte (envoye par un service en arriere-plan, ajoute separement).
/// Migration idempotente : ADD COLUMN IF NOT EXISTS pour rester sans risque sur
/// prod, au cas ou la colonne aurait deja ete creee manuellement.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260609120000_AddDailyReportEmailEnabled")]
public partial class AddDailyReportEmailEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE users ADD COLUMN IF NOT EXISTS daily_report_email_enabled boolean NOT NULL DEFAULT false;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE users DROP COLUMN IF EXISTS daily_report_email_enabled;");
    }
}
