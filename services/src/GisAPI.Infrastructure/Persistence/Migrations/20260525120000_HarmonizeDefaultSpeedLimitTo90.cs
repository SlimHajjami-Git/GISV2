using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 9 — harmonise la limite de vitesse par défaut des véhicules de
/// <c>120</c> à <c>90</c> km/h.
///
/// <para>Contexte : l'entité, la colonne DB et le frontend divergeaient
/// (120 backend / 90 frontend). L'alerte d'excès se déclenche à
/// <c>SpeedLimit + 20</c>, donc un défaut de 120 produisait une alerte à
/// 140 km/h — incohérent. 90 → alerte à 110 km/h (max autoroute Tunisie).</para>
///
/// <para>Deux changements, alignés sur la migration de bascule du code
/// de protection (#1311 → #9999) :</para>
/// <list type="number">
///   <item><description><b>DEFAULT</b> de la colonne <c>"SpeedLimit"</c>
///     sur <c>vehicles</c> : tout nouveau véhicule inséré sans valeur
///     explicite recevra 90.</description></item>
///   <item><description><b>Backfill</b> : les véhicules encore à l'ancien
///     défaut <c>120</c> passent à <c>90</c>. Toute valeur déjà
///     personnalisée par un opérateur (110, 80, etc.) reste intacte.</description></item>
/// </list>
///
/// <para>Idempotente : <c>SET DEFAULT</c> l'est, et l'<c>UPDATE WHERE
/// "SpeedLimit" = 120</c> ne matche plus aucune ligne après la première
/// passe. N'envoie AUCUNE commande aux boîtiers — pour propager les
/// nouvelles limites au hardware, utiliser
/// <c>POST /api/admin/speed-limits/sync-devices</c>.</para>
/// </summary>
public partial class HarmonizeDefaultSpeedLimitTo90 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE vehicles
                ALTER COLUMN ""SpeedLimit"" SET DEFAULT 90;
        ");

        // Ne touche QUE les lignes strictement égales à l'ancien défaut.
        migrationBuilder.Sql(@"
            UPDATE vehicles
            SET ""SpeedLimit"" = 90,
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
            WHERE ""SpeedLimit"" = 90;
        ");
    }
}
