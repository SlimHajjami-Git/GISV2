using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 7 — bascule du code de protection des commandes STOP/GO
/// envoyées aux trackers GPS, de <c>1311</c> vers <c>9999</c>.
///
/// <para>Deux changements appliqués atomiquement :</para>
/// <list type="number">
///   <item><description><b>DEFAULT</b> de la colonne :
///     <c>command_go</c> et <c>command_stop</c> sur <c>gps_devices</c>.
///     Tout nouveau device inséré sans valeur explicite recevra
///     <c>AJ+GO#9999\n</c> / <c>AJ+STOP#9999\n</c>.</description></item>
///   <item><description><b>Backfill</b> : toutes les lignes
///     <c>gps_devices</c> qui contiennent encore l'ancienne valeur
///     <c>AJ+GO#1311\n</c> ou <c>AJ+STOP#1311\n</c> sont mises à jour
///     vers <c>#9999</c>. Les lignes dont l'admin a entré une valeur
///     personnalisée (autre que les deux anciens defaults) ne sont
///     PAS touchées.</description></item>
/// </list>
///
/// <para>La migration est idempotente — la rejouer une seconde fois
/// est inoffensive : <c>ALTER COLUMN ... SET DEFAULT</c> est
/// idempotent, et le <c>UPDATE WHERE</c> ne match aucune ligne après
/// la première exécution.</para>
///
/// <para>Le Down restaure les anciens defaults <c>1311</c> et tente le
/// reverse-backfill, mais ne peut pas distinguer les valeurs déjà
/// personnalisées par l'admin avant la migration : un rollback
/// remettra TOUTES les lignes à <c>#1311</c> si elles avaient été
/// migrées par cette opération. Acceptable pour un secours, à
/// utiliser en connaissance de cause.</para>
/// </summary>
[Migration("20260501120000_UpdateGpsCommandPasswordTo9999")]
[DbContext(typeof(GisDbContext))]
public partial class UpdateGpsCommandPasswordTo9999 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Bascule des DEFAULT au niveau du schéma
        migrationBuilder.Sql(@"
            ALTER TABLE gps_devices
                ALTER COLUMN command_go   SET DEFAULT E'AJ+GO#9999\n',
                ALTER COLUMN command_stop SET DEFAULT E'AJ+STOP#9999\n';
        ");

        // 2. Backfill des lignes existantes qui portent encore l'ancien template.
        //    Volontairement précis : on ne touche QUE les valeurs strictement
        //    égales à l'ancien default. Toute valeur déjà personnalisée par
        //    un admin reste intacte.
        migrationBuilder.Sql(@"
            UPDATE gps_devices
            SET command_go = E'AJ+GO#9999\n',
                updated_at = NOW()
            WHERE command_go = E'AJ+GO#1311\n';
        ");
        migrationBuilder.Sql(@"
            UPDATE gps_devices
            SET command_stop = E'AJ+STOP#9999\n',
                updated_at   = NOW()
            WHERE command_stop = E'AJ+STOP#1311\n';
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE gps_devices
                ALTER COLUMN command_go   SET DEFAULT E'AJ+GO#1311\n',
                ALTER COLUMN command_stop SET DEFAULT E'AJ+STOP#1311\n';
        ");

        migrationBuilder.Sql(@"
            UPDATE gps_devices
            SET command_go = E'AJ+GO#1311\n',
                updated_at = NOW()
            WHERE command_go = E'AJ+GO#9999\n';
        ");
        migrationBuilder.Sql(@"
            UPDATE gps_devices
            SET command_stop = E'AJ+STOP#1311\n',
                updated_at   = NOW()
            WHERE command_stop = E'AJ+STOP#9999\n';
        ");
    }
}
