using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 6 (P5) - Ajout de la colonne "Date d'achat" separee de la
/// "Date de mise en circulation" sur les vehicules. Migration idempotente :
/// utilise ADD COLUMN IF NOT EXISTS pour rester sans risque sur prod au cas
/// ou la colonne aurait deja ete creee manuellement par un ancien script.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260424120000_AddPurchaseDateToVehicle")]
public partial class AddPurchaseDateToVehicle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE vehicles ADD COLUMN IF NOT EXISTS purchase_date timestamp with time zone NULL;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE vehicles DROP COLUMN IF EXISTS purchase_date;");
    }
}
