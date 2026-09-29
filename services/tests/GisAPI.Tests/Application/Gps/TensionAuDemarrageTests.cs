using FluentAssertions;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;
using GisAPI.Domain.Common;
using Xunit;

namespace GisAPI.Tests.Application.Gps;

/// <summary>
/// Monitoring — tension batterie des NEMS : on AFFICHE celle relevée au dernier
/// démarrage, on ALLUME le témoin sur la médiane des derniers démarrages
/// (Slim, 29/09/2026).
///
/// <para>Deux problèmes réglés d'un coup. L'octet « Batterie » (34-36) mélange deux
/// grandeurs — moteur tournant il porte l'alternateur (13,5 à 14,4 V) et ne dit rien
/// de la batterie ; seul l'instant du démarrage la montre. Et un démarrage bas isolé
/// ne prouve rien : mesuré sur 237 boîtiers et 4 jours de production TN, 9 des
/// 34 témoins allumés par la dernière mesure venaient d'un creux entouré de
/// démarrages sains (251 TU 8789 : 10,9 puis 12,3 | 12,5 | 12,5 | 12,3 V), tandis que
/// 5 batteries franchement faibles étaient MANQUÉES parce que leur dernier démarrage
/// était bon (235 TU 5540 : 13,1 puis 11,3 | 11,3 | 11,3 | 11,3 V).</para>
/// </summary>
public class TensionAuDemarrageTests
{
    private static readonly DateTime Demarrage = new(2026, 9, 29, 6, 12, 0, DateTimeKind.Utc);

    private static BatteryReadout.Result Nems(int? startRaw, int? medianRaw, DateTime? at = null) =>
        BatteryReadout.Compute("gps_type_1", voltageSensorReliable: false,
            deviceBatteryLevel: null, lastBatteryRaw: 84, lastPowerVoltage: 41,
            batteryStartRaw: startRaw, batteryStartAt: at ?? Demarrage,
            batteryStartMedianRaw: medianRaw);

    // ── Ce que le monitoring affiche ────────────────────────────────────────

    [Fact]
    public void Nems_AfficheLaTensionDuDernierDemarrage_PasLaDerniereTrame()
    {
        // Dernière trame à 84 brut (13,1 V) : c'est l'alternateur, il ne doit pas
        // s'afficher. Le démarrage, lui, était à 71 brut, soit 11,09 V.
        var r = Nems(startRaw: 71, medianRaw: 71);

        r.IsStartReading.Should().BeTrue();
        r.Volts.Should().Be(11.1, "11,09 V arrondi ; la dernière trame (13,1 V) n'est PAS affichée");
        r.Percent.Should().Be(5);
        r.MeasuredAt.Should().Be(Demarrage, "l'écran doit pouvoir dater la mesure");
        r.LowWarning.Should().BeTrue();
    }

    [Fact]
    public void Nems_NeConsultePlusLAudit_UnBoitierMasqueParLAncienVerdictSAffiche()
    {
        // voltage_sensor_reliable est faux (verdict hérité de l'octet 32-34) : la
        // tension s'affiche quand même. La garde « l'octet bouge-t-il » est portée par
        // le service qui écrit la valeur, pas par ce verdict-là.
        var r = Nems(startRaw: 80, medianRaw: 80);

        r.Volts.Should().Be(12.5);
        r.Percent.Should().Be(83);
        r.LowWarning.Should().BeFalse();
    }

    [Fact]
    public void Nems_AucunDemarrageExploitable_AfficheNA()
    {
        var r = BatteryReadout.Compute("gps_type_1", voltageSensorReliable: true,
            deviceBatteryLevel: 90, lastBatteryRaw: 84, lastPowerVoltage: 41,
            batteryStartRaw: null, batteryStartAt: null, batteryStartMedianRaw: null);

        r.IsStartReading.Should().BeTrue();
        r.Volts.Should().BeNull();
        r.Percent.Should().BeNull("le niveau enregistré sur le boîtier ne doit pas combler le N/A");
        r.MeasuredAt.Should().BeNull();
        r.MedianVolts.Should().BeNull();
        r.LowWarning.Should().BeFalse("pas de valeur, pas d'alerte");
    }

    [Theory]
    [InlineData(21)]   // octet de cap des firmwares R00C30d
    [InlineData(93)]   // au-delà de l'échelle : 14,5 V n'est pas une batterie au repos
    public void Nems_ValeurHorsBande_AfficheNA(int raw)
    {
        var r = Nems(startRaw: raw, medianRaw: raw);

        r.Volts.Should().BeNull();
        r.LowWarning.Should().BeFalse();
    }

    // ── Le témoin se juge sur la MÉDIANE, pas sur la dernière mesure ────────

    [Fact]
    public void CreuxIsole_AfficheLaTensionBasse_MaisNAllumePasLeTemoin()
    {
        // 251 TU 8789 : 10,9 V au dernier démarrage (radio oubliée), médiane 12,3 V.
        var r = Nems(startRaw: 70, medianRaw: 79);

        r.Volts.Should().Be(10.9, "l'exploitant doit voir la vraie dernière mesure");
        r.MedianVolts.Should().Be(12.3);
        r.LowWarning.Should().BeFalse("un seul démarrage bas ne condamne pas une batterie");
    }

    [Fact]
    public void BatterieFaible_AllumeLeTemoin_MemeApresUnBonDernierDemarrage()
    {
        // 235 TU 5540 : 13,1 V au dernier démarrage (long trajet qui a rechargé),
        // mais 11,3 V de médiane sur les précédents. L'ancienne règle la ratait.
        var r = Nems(startRaw: 84, medianRaw: 72);

        r.Volts.Should().Be(13.1);
        r.MedianVolts.Should().Be(11.3);
        r.LowWarning.Should().BeTrue("c'est la tendance qui compte, pas le dernier démarrage");
    }

    [Fact]
    public void PasAssezDeDemarrages_AfficheLaTension_SansTemoin()
    {
        // Médiane nulle = moins de 3 démarrages connus. On montre sans interpréter.
        var r = Nems(startRaw: 70, medianRaw: null);

        r.Volts.Should().Be(10.9);
        r.MedianVolts.Should().BeNull();
        r.LowWarning.Should().BeFalse();
    }

    [Theory]
    [InlineData(73, true)]    // 11,41 V
    [InlineData(74, false)]   // 11,56 V
    public void Nems_IconeAnomalie_SousOnzeVirguleCinqVolts(int medianRaw, bool alerte)
    {
        Nems(startRaw: 80, medianRaw: medianRaw).LowWarning.Should().Be(alerte);
    }

    // ── La médiane elle-même ────────────────────────────────────────────────

    [Fact]
    public void Mediane_IgnoreLeCreuxIsole_LaOuLaMoyenneSeFeraitTirer()
    {
        // 10,9 | 12,3 | 12,5 : moyenne 11,9 V (sous le seuil, faux positif),
        // médiane 12,3 V (au-dessus). C'est tout l'intérêt de la médiane.
        VoltageScale.MedianStartRaw(new short[] { 70, 79, 80 }).Should().Be(79);
    }

    [Fact]
    public void Mediane_SurNombrePair_PrendLaValeurDuBas()
    {
        // Devant une batterie, à égalité, on penche du côté prudent — et c'est aussi
        // ce que rend percentile_disc(0.5) côté PostgreSQL.
        VoltageScale.MedianStartRaw(new short[] { 70, 72, 80, 82 }).Should().Be(72);
    }

    [Fact]
    public void Mediane_NeRegardeQueLesVingtDerniersDemarrages()
    {
        // 20 démarrages sains récents, puis un long passé à 10 V : le passé ne doit
        // pas peser. La liste arrive du plus RÉCENT au plus ancien.
        var recents = Enumerable.Repeat<short>(80, 20);
        var vieux = Enumerable.Repeat<short>(64, 40);

        VoltageScale.MedianStartRaw(recents.Concat(vieux).ToList()).Should().Be(80);
    }

    [Fact]
    public void Mediane_MoinsDeTroisDemarrages_NeConclutPas()
    {
        VoltageScale.MedianStartRaw(new short[] { 70 }).Should().BeNull();
        VoltageScale.MedianStartRaw(new short[] { 70, 71 }).Should().BeNull(
            "une médiane sur deux valeurs, c'est le faux positif qu'on cherche à éviter");
        VoltageScale.MedianStartRaw(new short[] { 70, 71, 80 }).Should().Be(71);
    }

    // ── Teltonika : rien ne change ──────────────────────────────────────────

    [Fact]
    public void Teltonika_RegleInchangee_DerniereTrameSiLAuditLaValidee()
    {
        var r = BatteryReadout.Compute("teltonika", voltageSensorReliable: true,
            deviceBatteryLevel: null, lastBatteryRaw: 0, lastPowerVoltage: 125,
            batteryStartRaw: null, batteryStartAt: null, batteryStartMedianRaw: null);

        r.IsStartReading.Should().BeFalse();
        r.Volts.Should().Be(12.5);
        r.Percent.Should().Be(83);
        r.LowWarning.Should().BeFalse("leur icône reste pilotée par l'alerte de santé");
    }

    [Fact]
    public void Teltonika_AuditNonValide_AfficheNA()
    {
        var r = BatteryReadout.Compute("teltonika", voltageSensorReliable: false,
            deviceBatteryLevel: null, lastBatteryRaw: 0, lastPowerVoltage: 125,
            batteryStartRaw: 70, batteryStartAt: DateTime.UtcNow, batteryStartMedianRaw: 70);

        r.Volts.Should().BeNull();
        r.Percent.Should().BeNull();
    }

    [Fact]
    public void Teltonika_TensionPlafonneeA14Virgule4_EtNiveauDuBoitierPrioritaire()
    {
        var r = BatteryReadout.Compute("teltonika", true, deviceBatteryLevel: 55,
            lastBatteryRaw: null, lastPowerVoltage: 150,
            batteryStartRaw: null, batteryStartAt: null, batteryStartMedianRaw: null);

        r.Volts.Should().Be(14.4);
        r.Percent.Should().Be(55);
    }

    [Fact]
    public void StatsDto_ParDefaut_NEstPasUneTensionDeDemarrage()
    {
        // Le monitoring admin et tout autre appelant du record gardent l'ancien sens.
        var dto = new VehicleStatsDto(0, 0, null, null, null, null, false, true,
            TimeSpan.Zero, TimeSpan.Zero, null, null, null);

        dto.BatteryIsStartReading.Should().BeFalse();
        dto.BatteryMeasuredAt.Should().BeNull();
        dto.BatteryMedianVoltage.Should().BeNull();
    }

    // ── Garde « l'octet bouge-t-il » ────────────────────────────────────────

    [Fact]
    public void OctetFige_AvecRoulageEtArret_EstRejete()
    {
        // 244 TU 1249, 27/09/2026 : 1 124 trames toutes à 49 brut (7,66 V), en
        // roulant jusqu'à 88 km/h. Signature exacte de l'incident du 14/08/2026.
        VoltageScale.NemsByteMoves(minRaw: 49, maxRaw: 49, movingFrames: 558, restingFrames: 566)
            .Should().BeFalse();
    }

    [Fact]
    public void OctetFige_SurUnVehiculeGare_EstAccepte()
    {
        // 48 trames de veille à 82 brut sur un véhicule qui n'a pas bougé : un octet
        // constant y est normal. Sans ce plancher on condamnerait tout le parking —
        // 31 boîtiers le 27/09/2026 contre 1 avec.
        VoltageScale.NemsByteMoves(minRaw: 82, maxRaw: 82, movingFrames: 0, restingFrames: 48)
            .Should().BeTrue();
    }

    [Fact]
    public void OctetQuiVarie_MemeDUneSeuleUnite_EstAccepte()
    {
        VoltageScale.NemsByteMoves(minRaw: 79, maxRaw: 80, movingFrames: 600, restingFrames: 300)
            .Should().BeTrue();
    }

    [Fact]
    public void AucuneValeurDansLaJournee_NeCondamnePas()
    {
        VoltageScale.NemsByteMoves(minRaw: null, maxRaw: null, movingFrames: 0, restingFrames: 0)
            .Should().BeTrue("faute de preuve du contraire, on affiche");
    }

    // ── Temps réel (Broadcast SignalR) ──────────────────────────────────────

    [Fact]
    public void LiveBattery_Nems_NeDiffuseRien()
    {
        // La valeur affichée ne change qu'au démarrage suivant : une trame courante
        // porterait l'alternateur et effacerait la seule mesure utile.
        BroadcastPositionCommandHandler.LiveBattery("gps_type_1", true, 12.5, 83)
            .Should().Be(((double?)null, (int?)null));
        BroadcastPositionCommandHandler.LiveBattery("gps_type_1", false, 12.5, 83)
            .Should().Be(((double?)null, (int?)null));
    }

    [Fact]
    public void LiveBattery_Teltonika_Inchange()
    {
        BroadcastPositionCommandHandler.LiveBattery("teltonika", true, 12.7, 94)
            .Should().Be(((double?)12.7, (int?)94));
        BroadcastPositionCommandHandler.LiveBattery("teltonika", false, 12.7, 94)
            .Should().Be(((double?)null, (int?)null));
    }
}
