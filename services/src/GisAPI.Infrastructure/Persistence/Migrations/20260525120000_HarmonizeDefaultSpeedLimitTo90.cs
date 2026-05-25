using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 9 — harmonise la limite de vitesse par défaut des véhicules de
/// <c>120</c> à <c>110</c> km/h.
///
/// <para>Contexte : l'entité, la colonne DB et le frontend divergeaient
/// (120 backend / 90 frontend). Combiné à la suppression de la marge de
/// tolérance (l'alerte se déclenche désormais à la limite exacte, plus à
/// limite + 20), un défaut de 120 n'avait plus de sens. 110 = max
/// autoroute Tunisie → alerte pile à 110.</para>
///
/// <para>Deux changements, alignés sur la migration de bascule du code
/// de protection (#1311 → #9999) :</para>
/// <list type="number">
///   <item><description><b>DEFAULT</b> de la colonne <c>"SpeedLimit"</c>
///     sur <c>vehicles</c> : tout nouveau véhicule inséré sans valeur
///     explicite recevra 110.</description></item>
///   <item><description><b>Backfill</b> : les véhicules encore à l'ancien
///     défaut <c>120</c> passent à <c>110</c>. Toute valeur déjà
///     personnalisée par un opérateur (90, 80, 70…) reste intacte.</description></item>
/// </list>
///
/// <para>Idempotente : <c>SET DEFAULT</c> l'est, et l'<c>UPDATE WHERE
/// "SpeedLimit" = 120</c> ne matche plus aucune ligne après la première
/// passe. N'envoie AUCUNE commande aux boîtiers — pour propager les
/// nouvelles limites au hardware, utiliser
/// <c>POST /api/admin/speed-limits/sync-devices</c>.</para>
/// </summary>
[Migration("20260525120000_HarmonizeDefaultSpeedLimitTo90")]
[DbContext(typeof(GisDbContext))]
public partial class HarmonizeDefaultSpeedLimitTo90 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE vehicles
                ALTER COLUMN ""SpeedLimit"" SET DEFAULT 110;
        ");

        // Ne touche QUE les lignes strictement égales à l'ancien défaut.
        migrationBuilder.Sql(@"
            UPDATE vehicles
            SET ""SpeedLimit"" = 110,
                updated_at = NOW()
            WHERE ""SpeedLimit"" = 120;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE vehicles
                ALTER COLUMN ""SpeedLimit"" SET DEFAULT 120;
        ");

        migrationBuilder.Sql(@"
            UPDATE vehicles
            SET ""SpeedLimit"" = 120,
                updated_at = NOW()
            WHERE ""SpeedLimit"" = 110;
        ");
    }
}
