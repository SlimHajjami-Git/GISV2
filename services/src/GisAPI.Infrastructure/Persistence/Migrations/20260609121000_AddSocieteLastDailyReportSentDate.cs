using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute societes.last_daily_report_sent_date : garde anti-doublon PERSISTANTE
/// pour l'envoi du rapport journalier de la flotte. Le service en arriere-plan ne
/// renvoie pas le rapport d'une journee a une societe qui l'a deja recu, meme apres
/// un redemarrage de l'API (sinon un deploiement apres 06:00 renverrait tout).
/// Migration idempotente (ADD COLUMN IF NOT EXISTS) pour rester sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260609121000_AddSocieteLastDailyReportSentDate")]
public partial class AddSocieteLastDailyReportSentDate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE societes ADD COLUMN IF NOT EXISTS last_daily_report_sent_date date NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE societes DROP COLUMN IF EXISTS last_daily_report_sent_date;");
    }
}
