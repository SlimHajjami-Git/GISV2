using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Remet la définition du plan « Gestion de parc sans GPS » (code plan-basique)
/// en conformité avec ce que le produit vend.
///
/// POURQUOI UNE MIGRATION ET PAS LE SEED — le seed de Program.cs est create-only :
/// il ne touche jamais un plan déjà créé. Les serveurs ont donc dérivé, chacun dans
/// une direction différente et sans que personne s'en aperçoive :
///
///   plan-basique      | code (seed) | TN prod | DZ test
///   module_vehicles   |    true     | FALSE   | true
///   module_reports    |    true     | true    | FALSE
///   module_tours      |    false    | true    | true
///
/// Sur TN, module_vehicles=false retirait l'écran Véhicules d'un plan de gestion
/// de véhicules : le client ne pouvait alimenter aucun des écrans en aval
/// (Entretiens, Dépenses, Documents travaillent tous sur cette liste). Sur DZ,
/// module_reports=false rendait inatteignables les trois rapports que le plan
/// active pourtant explicitement.
///
/// On corrige par migration plutôt qu'en forçant les drapeaux à chaque démarrage :
/// une réconciliation permanente annulerait en silence tout réglage fait par
/// l'administrateur au prochain redémarrage de l'API. Une migration s'applique une
/// seule fois, est tracée dans __EFMigrationsHistory, et laisse ensuite la main à
/// l'écran d'administration.
///
/// Ne touche QUE la ligne plan-basique et uniquement les drapeaux structurellement
/// liés à l'absence de boîtier. Les prix, quotas et autres plans sont intacts.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260731120000_FixNoGpsPlanDefinition")]
public partial class FixNoGpsPlanDefinition : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
UPDATE subscription_types SET
    -- Sans ce module, le client ne peut ni créer ni modifier un véhicule : les
    -- écrans Entretiens, Dépenses et Documents restent vides à jamais.
    module_vehicles = TRUE,
    -- Porte d'entrée de /reports. Le plan active trois rapports (kilométrage,
    -- coûts, entretien) ; le tri fin se fait par les drapeaux report_*.
    module_reports = TRUE,
    -- Une tournée suppose un itinéraire suivi en temps réel : sans boîtier
    -- l'écran ne peut rien afficher. La colonne vaut TRUE par défaut, il faut
    -- donc l'éteindre explicitement.
    module_tours = FALSE,
    -- Les pleins sont saisis à la main (fuel_entries porte déjà odometer_km) :
    -- le module reste ouvert, c'est le rapport GPS qui est fermé plus bas.
    module_fuel = TRUE,
    -- Monitoring et géofences n'ont aucune source de données sans boîtier.
    module_monitoring = FALSE,
    module_geofences = FALSE,
    -- Rapports 100 % GPS : ils sortiraient vides ou, pire, fabriqueraient des
    -- chiffres à partir d'une distance nulle.
    report_trips = FALSE,
    report_fuel = FALSE,
    report_speed = FALSE,
    report_stops = FALSE,
    report_daily = FALSE,
    report_speed_infraction = FALSE,
    report_driving_behavior = FALSE,
    gps_tracking = FALSE,
    history_playback = FALSE,
    real_time_alerts = FALSE,
    updated_at = NOW()
WHERE code = 'plan-basique';
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Pas de retour arrière : on ne sait pas quelle configuration divergente
        // chaque serveur portait avant, et la restaurer serait restaurer le défaut.
    }
}
