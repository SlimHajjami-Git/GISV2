using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute societes.auto_suspend_enabled : interrupteur PAR SOCIÉTÉ de la
/// suspension automatique à l'expiration de l'abonnement (grâce de 7 j).
/// true (défaut) = blocage automatique après la grâce ; false = la société
/// expirée garde l'accès (bannière rouge, marquée impayée côté supervision)
/// et seule la suspension manuelle du sys_admin coupe.
/// Migration idempotente (ADD COLUMN IF NOT EXISTS) — sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260713100000_AddSocieteAutoSuspendEnabled")]
public partial class AddSocieteAutoSuspendEnabled : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE societes ADD COLUMN IF NOT EXISTS auto_suspend_enabled boolean NOT NULL DEFAULT TRUE;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE societes DROP COLUMN IF EXISTS auto_suspend_enabled;");
    }
}
