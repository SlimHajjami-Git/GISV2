using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Services.Tours;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// Règles PURES du suivi à deux sources et des déclarations du chauffeur (lot 1,
/// migration 051). Ce sont elles que le moniteur et le contrôleur appliquent ; les
/// seuils viennent des trames réelles de TN (relecture du 18/09/2026).
/// </summary>
public class SuiviDeuxSourcesTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    private static TrackingSourceSelector.DeviceState Boitier(int? secondes, bool contact = true) =>
        new(secondes.HasValue ? Now.AddSeconds(-secondes.Value) : null, contact);

    private static TrackingSourceSelector.PhoneState Telephone(int? secondes, double? precision = 20, bool simule = false) =>
        new(secondes.HasValue ? Now.AddSeconds(-secondes.Value) : null, precision, simule);

    // ── Boîtier vivant ou pas ──────────────────────────────────────────────────

    [Theory]
    [InlineData(60, true, true)]      // 1 min contact mis : vivant
    [InlineData(200, true, false)]    // 3 min 20 contact mis : mort
    [InlineData(1500, false, true)]   // 25 min contact coupé : encore vivant (HERTZ envoie toutes les 30 min à l'arrêt)
    [InlineData(2200, false, false)]  // 36 min contact coupé : mort
    public void Le_boitier_est_vivant_selon_le_contact(int secondes, bool contact, bool attendu) =>
        TrackingSourceSelector.IsDeviceAlive(Now, Boitier(secondes, contact)).Should().Be(attendu);

    [Theory]
    [InlineData(60, 20.0, false, true)]
    [InlineData(200, 20.0, false, false)]   // trop vieux
    [InlineData(60, 150.0, false, false)]   // trop imprécis
    [InlineData(60, 20.0, true, false)]     // position simulée
    public void Le_telephone_est_vivant_s_il_est_recent_precis_et_vrai(int secondes, double precision, bool simule, bool attendu) =>
        TrackingSourceSelector.IsPhoneAlive(Now, Telephone(secondes, precision, simule)).Should().Be(attendu);

    // ── Choix de la source ─────────────────────────────────────────────────────

    [Fact]
    public void Le_boitier_a_la_priorite_quand_les_deux_sont_vivants()
    {
        var c = TrackingSourceSelector.Choose(Now, Boitier(30), Telephone(30), previous: null, recoveryCount: 0);
        c.Source.Should().Be(TrackingSourceSelector.Device);
        (c.DeviceAlive, c.PhoneAlive).Should().Be((true, true));
    }

    [Fact]
    public void Le_telephone_prend_le_relais_quand_le_boitier_se_tait()
    {
        var c = TrackingSourceSelector.Choose(Now, Boitier(600), Telephone(30), TrackingSourceSelector.Device, 0);
        c.Source.Should().Be(TrackingSourceSelector.Phone);
    }

    [Fact]
    public void Aucune_source_quand_les_deux_se_taisent()
    {
        TrackingSourceSelector.Choose(Now, Boitier(null), Telephone(null), TrackingSourceSelector.Phone, 0)
            .Source.Should().Be(TrackingSourceSelector.None);
        TrackingSourceSelector.Choose(Now, Boitier(600), Telephone(30, simule: true), null, 0)
            .Source.Should().Be(TrackingSourceSelector.None, "une position simulée n'est pas une source");
    }

    [Fact]
    public void Le_retour_du_boitier_attend_deux_cycles_pour_ne_pas_faire_clignoter_la_pastille()
    {
        // Le téléphone assurait le relais ; le boîtier redonne signe de vie.
        var c1 = TrackingSourceSelector.Choose(Now, Boitier(30), Telephone(30), TrackingSourceSelector.Phone, 0);
        c1.Source.Should().Be(TrackingSourceSelector.Phone, "premier cycle vivant : on attend");
        c1.RecoveryCount.Should().Be(1);

        var c2 = TrackingSourceSelector.Choose(Now.AddSeconds(30), Boitier(30), Telephone(30), c1.Source, c1.RecoveryCount);
        c2.Source.Should().Be(TrackingSourceSelector.Device, "deuxième cycle vivant d'affilée : le boîtier reprend");
        c2.RecoveryCount.Should().Be(0);
    }

    [Fact]
    public void Un_boitier_qui_revient_alors_que_le_telephone_est_mort_reprend_tout_de_suite()
    {
        var c = TrackingSourceSelector.Choose(Now, Boitier(30), Telephone(null), TrackingSourceSelector.Phone, 0);
        c.Source.Should().Be(TrackingSourceSelector.Device, "il n'y a rien à ménager : le relais est parti");
    }

    [Theory]
    [InlineData(true, "eco")]
    [InlineData(false, "full")]
    public void La_cadence_du_telephone_suit_le_boitier(bool boitierVivant, string attendu) =>
        TrackingSourceSelector.PhoneMode(boitierVivant).Should().Be(attendu);

    // ── Heure déclarée ─────────────────────────────────────────────────────────

    [Fact]
    public void L_heure_du_telephone_est_gardee_quand_elle_est_plausible()
    {
        var reference = Now.AddMinutes(-40);
        DriverTourRules.BoundDeclaredTime(Now.AddMinutes(-3), Now, reference).Should().Be(Now.AddMinutes(-3), "file hors ligne rejouée");
        DriverTourRules.BoundDeclaredTime(Now.AddMinutes(1), Now, reference).Should().Be(Now.AddMinutes(1), "2 min d'avance tolérées");
    }

    [Fact]
    public void Une_heure_de_telephone_absurde_est_remplacee_par_celle_du_serveur()
    {
        var reference = Now.AddMinutes(-40);
        DriverTourRules.BoundDeclaredTime(Now.AddHours(2), Now, reference).Should().Be(Now, "dans le futur");
        DriverTourRules.BoundDeclaredTime(reference.AddMinutes(-10), Now, reference).Should().Be(Now, "avant la tournée");
        DriverTourRules.BoundDeclaredTime(null, Now, reference).Should().Be(Now);
    }

    // ── Heure d'une déclaration : sentAt (relecture du 21/09/2026, R10c / Wc18) ──

    [Fact]
    public void Une_horloge_en_retard_en_direct_n_est_pas_un_faux_rejeu()
    {
        // Téléphone en retard de 4 min, geste envoyé aussitôt : avant, l'heure retenue
        // (maintenant − 4 min) suffisait à le déclarer « rejoué ».
        var reference = Now.AddHours(-1);
        var retard = TimeSpan.FromMinutes(-4);

        var lu = DriverTourRules.ReadDeclarationTime(Now + retard - TimeSpan.FromSeconds(1), Now + retard, Now, reference);

        lu.Replayed.Should().BeFalse("sentAt − clientTime = 1 s");
        lu.GestureAt.Should().Be(Now.AddSeconds(-1), "heure du geste corrigée du décalage");
        lu.DeclaredAt.Should().Be(Now.AddSeconds(-1));
    }

    [Fact]
    public void Un_rejeu_se_reconnait_sur_la_meme_horloge_quel_que_soit_son_decalage()
    {
        // Horloge en retard de 10 min ; geste 25 min avant l'envoi (file hors ligne).
        var reference = Now.AddHours(-2);
        var retard = TimeSpan.FromMinutes(-10);

        var lu = DriverTourRules.ReadDeclarationTime(Now + retard - TimeSpan.FromMinutes(25), Now + retard, Now, reference);

        lu.Replayed.Should().BeTrue();
        lu.GestureAt.Should().Be(Now.AddMinutes(-25));
        lu.DeclaredAt.Should().Be(Now.AddMinutes(-25));
    }

    [Fact]
    public void Un_rejeu_borne_a_maintenant_reste_un_rejeu_et_garde_l_heure_du_geste()
    {
        // Geste antérieur à la référence − 5 min : l'heure retenue est « maintenant », mais la
        // trame du boîtier doit être lue à l'heure du geste.
        var reference = Now.AddMinutes(-30);

        var lu = DriverTourRules.ReadDeclarationTime(Now.AddMinutes(-50), Now, Now, reference);

        lu.DeclaredAt.Should().Be(Now);
        lu.Replayed.Should().BeTrue();
        lu.GestureAt.Should().Be(Now.AddMinutes(-50));
    }

    [Fact]
    public void Sans_sentAt_la_regle_d_avant_s_applique()
    {
        var reference = Now.AddHours(-1);
        var lu = DriverTourRules.ReadDeclarationTime(Now.AddMinutes(-25), null, Now, reference);
        (lu.DeclaredAt, lu.GestureAt, lu.Replayed).Should().Be((Now.AddMinutes(-25), Now.AddMinutes(-25), true));
    }

    [Fact]
    public void Le_depart_d_une_tournee_planifiee_se_borne_a_son_envoi_et_non_a_l_heure_prevue()
    {
        // Wc18 : prévue à 08:00, envoyée à 06:00, « Je pars » à 07:40 hors ligne, rejoué à 08:30.
        var prevue = Now.AddMinutes(-30);
        var tour = new Tour { ScheduledStartTime = prevue, SentAt = Now.AddMinutes(-150) };
        var reference = DriverTourRules.DepartureReference(tour);

        DriverTourRules.ReadDeclarationTime(Now.AddMinutes(-50), Now, Now, reference).DeclaredAt
            .Should().Be(Now.AddMinutes(-50), "départ anticipé rejoué : l'heure du geste est gardée");

        // Renvoyée après une première ouverture : la plus ancienne des deux fait foi.
        var renvoyee = new Tour { ScheduledStartTime = prevue, SentAt = Now.AddMinutes(-40), OpenedAt = Now.AddMinutes(-120) };
        DriverTourRules.DepartureReference(renvoyee).Should().Be(Now.AddMinutes(-120));
    }

    [Theory]
    [InlineData(-4 * 60, -4 * 60)]   // 4 min de retard : corrigé
    [InlineData(-20, 0)]             // 20 s : le réseau, pas une horloge fausse
    [InlineData(null, 0)]
    public void Le_decalage_d_horloge_se_mesure_sur_sentAt(int? decalageS, int attenduS)
    {
        DateTime? sentAt = decalageS.HasValue ? Now.AddSeconds(decalageS.Value) : null;
        DriverTourRules.ClockSkew(sentAt, Now).Should().Be(TimeSpan.FromSeconds(-attenduS));
    }

    // ── État du boîtier : trames écrémées à l'arrêt (R2c) ───────────────────────

    [Fact]
    public void Une_communication_plus_recente_qu_une_trame_contact_coupe_prolonge_le_boitier()
    {
        var etat = TrackingSourceSelector.DeviceStateOf(Now.AddMinutes(-50), false, Now.AddMinutes(-20), ignitionOnSince: false);
        etat.Should().Be(new TrackingSourceSelector.DeviceState(Now.AddMinutes(-20), false));
        TrackingSourceSelector.IsDeviceAlive(Now, etat).Should().BeTrue();
    }

    [Fact]
    public void Une_communication_ne_prolonge_ni_une_trame_contact_mis_ni_un_vehicule_reparti()
    {
        TrackingSourceSelector.DeviceStateOf(Now.AddMinutes(-5), true, Now.AddMinutes(-1), ignitionOnSince: false)
            .LastAt.Should().Be(Now.AddMinutes(-5), "contact mis : l'ingest n'écrème pas, une trame manquante est une vraie perte");
        TrackingSourceSelector.DeviceStateOf(Now.AddMinutes(-50), false, Now.AddMinutes(-1), ignitionOnSince: true)
            .LastAt.Should().Be(Now.AddMinutes(-50), "reparti depuis, sans position valide");
        TrackingSourceSelector.DeviceStateOf(null, false, Now.AddMinutes(-1), ignitionOnSince: false)
            .LastAt.Should().BeNull("aucune trame stockée : rien ne dit où est le véhicule");
    }

    [Fact]
    public void Une_position_hors_de_la_fenetre_de_la_tournee_est_ignoree()
    {
        var depart = Now.AddHours(-1);
        DriverTourRules.IsPositionInWindow(Now.AddMinutes(-10), depart, Now).Should().BeTrue();
        DriverTourRules.IsPositionInWindow(depart.AddMinutes(-10), depart, Now).Should().BeFalse("avant le départ");
        DriverTourRules.IsPositionInWindow(Now.AddMinutes(10), depart, Now).Should().BeFalse("dans le futur");
        DriverTourRules.IsPositionInWindow(depart.AddHours(13), depart, depart.AddHours(14)).Should().BeFalse("plus de 12 h après le départ");
    }

    // ── Déclaration d'arrivée ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null, false)]         // aucune source : jamais refusée
    [InlineData(3000, null, false)]         // une seule source loin : avertissement, pas refus
    [InlineData(3000, 200, false)]          // les deux sources en désaccord : le téléphone est près
    [InlineData(3000, 2500, true)]          // les deux loin : refusée
    public void L_arrivee_n_est_refusee_que_si_boitier_et_telephone_la_contredisent(int? boitierM, int? telephoneM, bool refusee) =>
        DriverTourRules.IsArrivalDeclarationRefused(boitierM, telephoneM).Should().Be(refusee);

    [Fact]
    public void Declarer_une_etape_attendue_la_valide_avec_la_source_chauffeur()
    {
        var wp = new TourWaypoint { Type = "waypoint", WaypointStatus = "pending" };

        DriverTourRules.DeclareArrival(wp, Now, 120).Should().BeTrue();

        (wp.IsCompleted, wp.WaypointStatus, wp.ArrivalSource).Should().Be((true, "completed", DriverTourRules.SourceDriver));
        wp.ActualArrivalTime.Should().Be(Now);
        wp.DriverArrivedAt.Should().Be(Now);
        wp.DriverDeclarationDistanceM.Should().Be(120);
        DriverTourRules.IsUnconfirmed(wp).Should().BeTrue("validée par le chauffeur seul");
    }

    [Fact]
    public void Declarer_une_etape_deja_detectee_ne_change_ni_son_heure_ni_sa_source()
    {
        var wp = new TourWaypoint { Type = "waypoint" };
        TourPlanning.MarkReached(wp, Now.AddMinutes(-7));
        wp.ArrivalSource = DriverTourRules.SourceDevice;

        DriverTourRules.DeclareArrival(wp, Now, 50).Should().BeFalse();

        wp.ActualArrivalTime.Should().Be(Now.AddMinutes(-7));
        wp.ArrivalSource.Should().Be(DriverTourRules.SourceDevice);
        wp.DriverArrivedAt.Should().Be(Now, "la déclaration est notée à part");
        DriverTourRules.IsUnconfirmed(wp).Should().BeFalse();
    }

    [Fact]
    public void La_detection_confirme_une_arrivee_declaree_et_l_heure_detectee_fait_foi()
    {
        var wp = new TourWaypoint { Type = "waypoint", WaypointStatus = "pending" };
        DriverTourRules.DeclareArrival(wp, Now, null);

        DriverTourRules.ConfirmDeclaredArrival(wp, Now.AddMinutes(4), DriverTourRules.SourcePhone);

        wp.ActualArrivalTime.Should().Be(Now.AddMinutes(4));
        wp.ArrivalSource.Should().Be(DriverTourRules.SourcePhone);
        wp.DriverArrivedAt.Should().Be(Now, "ce que le chauffeur a dit reste visible");
        DriverTourRules.IsUnconfirmed(wp).Should().BeFalse();
    }

    // ── Démarrage au « Je pars » ───────────────────────────────────────────────

    [Fact]
    public void Une_tournee_envoyee_a_un_chauffeur_ne_demarre_plus_a_l_heure_mais_alerte_apres_15_min()
    {
        var t = new Tour { Status = "planned", DriverId = 4, SentAt = Now.AddHours(-2), ScheduledStartTime = Now.AddMinutes(-10) };
        DriverTourRules.StartsOnDriverDeparture(t).Should().BeTrue();
        DriverTourRules.IsNotStartedAlertDue(t, Now).Should().BeFalse("10 min : pas encore");
        DriverTourRules.IsNotStartedAlertDue(t, Now.AddMinutes(6)).Should().BeTrue("16 min après l'heure prévue");

        var sansEnvoi = new Tour { Status = "planned", DriverId = 4, SentAt = null, ScheduledStartTime = Now.AddMinutes(-30) };
        DriverTourRules.StartsOnDriverDeparture(sansEnvoi).Should().BeFalse("jamais envoyée : démarrage automatique conservé");
    }
}
