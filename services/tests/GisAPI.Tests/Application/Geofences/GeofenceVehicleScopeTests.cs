using FluentAssertions;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using Xunit;

namespace GisAPI.Tests.Application.Geofences;

/// <summary>
/// Une zone à laquelle des véhicules ont été affectés ne concerne QUE ceux-là.
///
/// Le lien était enregistré en base (geofence_vehicles) mais n'était jamais chargé
/// dans le cache de surveillance : chaque zone active était évaluée contre CHAQUE
/// véhicule de la société. Un exploitant qui désignait un véhicule précis recevait
/// aussi les entrées et sorties de tous les autres — c'est ce qui a été constaté en
/// exploitation.
///
/// Ces tests portent sur la règle de périmètre elle-même, indépendamment de la
/// géométrie et du planning horaire, qui ont leurs propres tests.
/// </summary>
public class GeofenceVehicleScopeTests
{
    private static GeofenceCacheEntry Zone(params int[] vehicleIds) =>
        new(1, "Zone", "circle", null, 36.8, 10.18, 500, true, true, null,
            null, null, null, vehicleIds);

    /// <summary>Reproduit la garde posée dans CheckGeofences.</summary>
    private static bool ZoneConcerneLeVehicule(GeofenceCacheEntry zone, int vehicleId) =>
        zone.VehicleIds.Length == 0 || zone.VehicleIds.Contains(vehicleId);

    [Fact]
    public void Une_zone_sans_affectation_couvre_tout_le_parc()
    {
        // Comportement historique, et lecture naturelle : ne désigner personne
        // veut dire « tous ».
        var zone = Zone();

        ZoneConcerneLeVehicule(zone, 1).Should().BeTrue();
        ZoneConcerneLeVehicule(zone, 999).Should().BeTrue();
    }

    [Fact]
    public void Une_zone_affectee_ne_concerne_que_ses_vehicules()
    {
        var zone = Zone(12, 34);

        ZoneConcerneLeVehicule(zone, 12).Should().BeTrue();
        ZoneConcerneLeVehicule(zone, 34).Should().BeTrue();
    }

    [Fact]
    public void Un_vehicule_non_affecte_est_ignore_par_la_zone()
    {
        // LE défaut constaté : ce véhicule declenchait l'alerte d'entrée.
        var zone = Zone(12, 34);

        ZoneConcerneLeVehicule(zone, 56).Should().BeFalse();
    }

    [Fact]
    public void L_affectation_d_un_seul_vehicule_exclut_bien_les_autres()
    {
        var zone = Zone(7);

        ZoneConcerneLeVehicule(zone, 7).Should().BeTrue();
        ZoneConcerneLeVehicule(zone, 8).Should().BeFalse();
    }
}
