using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute user_device_tokens.device_id : identifiant stable de l'appareil
/// (Android ID) envoyé par l'appli mobile à l'enregistrement du jeton FCM.
/// Chaque réinstallation/MAJ de l'appli créait un nouveau jeton SANS désactiver
/// les anciens (encore livrables pendant des semaines) → l'utilisateur recevait
/// N copies de chaque notification (4 constatées en test). Avec le device_id,
/// l'enregistrement d'un nouveau jeton désactive ceux du même téléphone.
/// Migration idempotente (ADD COLUMN IF NOT EXISTS) pour rester sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260707210000_AddDeviceTokenDeviceId")]
public partial class AddDeviceTokenDeviceId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE user_device_tokens ADD COLUMN IF NOT EXISTS device_id varchar(100) NULL;");
        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS ix_user_device_tokens_device_id ON user_device_tokens (device_id) WHERE device_id IS NOT NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_user_device_tokens_device_id;");
        migrationBuilder.Sql("ALTER TABLE user_device_tokens DROP COLUMN IF EXISTS device_id;");
    }
}
