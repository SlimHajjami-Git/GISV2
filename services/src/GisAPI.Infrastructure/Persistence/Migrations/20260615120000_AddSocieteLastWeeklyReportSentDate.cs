using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute societes.last_weekly_report_sent_date : garde anti-doublon PERSISTANTE
/// pour le récapitulatif HEBDOMADAIRE de la flotte (envoyé le lundi). Le service en
/// arrière-plan ne renvoie pas le récap d'une semaine à une société qui l'a déjà reçu,
/// même après un redémarrage de l'API. Migration idempotente (ADD COLUMN IF NOT EXISTS)
/// pour rester sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260615120000_AddSocieteLastWeeklyReportSentDate")]
public partial class AddSocieteLastWeeklyReportSentDate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE societes ADD COLUMN IF NOT EXISTS last_weekly_report_sent_date date NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE societes DROP COLUMN IF EXISTS last_weekly_report_sent_date;");
    }
}
