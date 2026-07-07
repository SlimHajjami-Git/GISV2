using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute vehicle_costs.details_json : le détail (lignes) d'une facture scannée,
/// stocké en JSON <c>[{"label","amount","category"}]</c> sur la dépense UNIQUE
/// créée par le scan — une facture = une ligne dans la liste des dépenses, le
/// détail décortiqué s'affiche dans le panneau de la dépense. Migration
/// idempotente (ADD COLUMN IF NOT EXISTS) pour rester sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260707150000_AddVehicleCostDetailsJson")]
public partial class AddVehicleCostDetailsJson : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE vehicle_costs ADD COLUMN IF NOT EXISTS details_json text NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE vehicle_costs DROP COLUMN IF EXISTS details_json;");
    }
}
