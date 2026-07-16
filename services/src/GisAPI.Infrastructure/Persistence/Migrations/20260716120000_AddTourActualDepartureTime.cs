using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute tours."ActualDepartureTime" : première mise en mouvement réelle du
/// véhicule (sortie du rayon du point de départ), persistée par
/// TourMonitoringService. Distincte d'ActualStartTime (clic « démarrer ») :
/// l'écart entre les deux = attente avant départ, désormais exclue de la durée
/// réelle de conduite. Migration idempotente — sans risque en prod.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260716120000_AddTourActualDepartureTime")]
public partial class AddTourActualDepartureTime : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE tours ADD COLUMN IF NOT EXISTS \"ActualDepartureTime\" timestamp without time zone;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE tours DROP COLUMN IF EXISTS \"ActualDepartureTime\";");
    }
}
