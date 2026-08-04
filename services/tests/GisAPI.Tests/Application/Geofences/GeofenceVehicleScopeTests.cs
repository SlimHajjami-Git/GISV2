using FluentAssertions;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using Xunit;

namespace GisAPI.Tests.Application.Geofences;

/// <summary>
/// Périmètre et reconstruction de l'état des zones, via
/// <see cref="BroadcastPositionCommandHandler.ComputeInsideZones"/> — la fonction
/// que la surveillance ET le réensemencement au démarrage utilisent réellement.
///
/// Deux règles couvertes :
///  1. une zone à laquelle des véhicules ont été affectés ne concerne QUE ceux-là
///     (le lien geofence_vehicles était enregistré mais jamais lu : un exploitant
///     recevait les entrées de véhicules qu'il n'avait pas désignés) ;
///  2. l'état « à l'intérieur » se reconstruit depuis la dernière position en
///     base après un redémarrage (l'état ne vit qu'en mémoire : chaque
///     redémarrage produisait une fausse « entrée » par véhicule stationné dans
///     sa zone).
/// </summary>
public class GeofenceVehicleScopeTests
{
    // Cercle de 500 m centré sur Tunis. DansLaZone = le centre ;
    // HorsZone = ~15 km plus loin, sans ambiguïté géométrique.
    private const double ZoneLat = 36.8, ZoneLng = 10.18;
    private const double DansLat = 36.8, DansLng = 10.18;
    private const double HorsLat = 36.9, HorsLng = 10.3;

    private static GeofenceCacheEntry Zone(int id = 1, params int[] vehicleIds) =>
        new(id, "Zone", "circle", null, ZoneLat, ZoneLng, 500, true, true, null,
            null, null, null, vehicleIds);

    // ── Règle 1 : périmètre des véhicules affectés ──

    [Fact]
    public void Une_zone_sans_affectation_couvre_tout_le_parc()
    {
        // Ne désigner personne veut dire « tous » : comportement historique,
        // les zones existantes ne changent pas.
        var zones = new[] { Zone() };

        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 1, zones)
            .Should().Contain(1);
        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 999, zones)
            .Should().Contain(1);
    }

    [Fact]
    public void Une_zone_affectee_ne_concerne_que_ses_vehicules()
    {
        var zones = new[] { Zone(1, 12, 34) };

        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 12, zones)
            .Should().Contain(1);
        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 34, zones)
            .Should().Contain(1);
    }

    [Fact]
    public void Un_vehicule_non_affecte_est_ignore_meme_au_centre_de_la_zone()
    {
        // LE défaut constaté en exploitation : ce véhicule déclenchait l'alerte.
        var zones = new[] { Zone(1, 12, 34) };

        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 56, zones)
            .Should().BeEmpty();
    }

    [Fact]
    public void Chaque_zone_applique_son_propre_perimetre()
    {
        // Deux zones au même endroit : l'une réservée au véhicule 7, l'autre
        // ouverte à tous. Le véhicule 8 ne voit que la seconde.
        var zones = new[] { Zone(1, 7), Zone(2) };

        var vues = BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 8, zones);

        vues.Should().BeEquivalentTo(new[] { 2 });
    }

    // ── Règle 2 : reconstruction de l'état après redémarrage ──
    //
    // La boucle de surveillance compare l'état semé (wasInside) à la position
    // courante (isInside) : ces tests vérifient que le semis produit l'état qui
    // évite la fausse « entrée » ET préserve la vraie.

    [Fact]
    public void Un_vehicule_stationne_dans_sa_zone_est_seme_comme_deja_dedans()
    {
        // Dernière position en base : dans la zone. Le semis doit le dire, sinon
        // la première position après redémarrage refait une fausse « entrée ».
        var zones = new[] { Zone() };

        var seme = BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 1, zones);

        seme.Should().Contain(1, "wasInside sera vrai : pas d'alerte pour un véhicule qui n'a pas bougé");
    }

    [Fact]
    public void Une_entree_reelle_pendant_le_redemarrage_n_est_pas_perdue()
    {
        // Dernière position en base : HORS zone. Le semis dit « dehors », donc si
        // la position courante est dedans, l'entrée est détectée — le
        // redémarrage n'avale pas l'événement.
        var zones = new[] { Zone() };

        var seme = BroadcastPositionCommandHandler.ComputeInsideZones(HorsLat, HorsLng, 1, zones);

        seme.Should().BeEmpty("wasInside sera faux : l'entrée réelle déclenchera bien l'alerte");
    }

    [Fact]
    public void Le_semis_respecte_le_perimetre_des_vehicules_affectes()
    {
        // Un véhicule non affecté ne doit pas être semé « dedans » : le jour où
        // on l'affecte, son état repartirait faussé.
        var zones = new[] { Zone(1, 12) };

        BroadcastPositionCommandHandler.ComputeInsideZones(DansLat, DansLng, 56, zones)
            .Should().BeEmpty();
    }
}
