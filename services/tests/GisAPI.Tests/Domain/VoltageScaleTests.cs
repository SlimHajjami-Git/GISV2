using FluentAssertions;
using GisAPI.Domain.Common;
using Xunit;

namespace GisAPI.Tests.Domain;

/// <summary>
/// Règle d'affichage de la batterie.
///
/// <para>Deux dates structurent ces cas. Le <b>14/08/2026</b>, l'application a
/// affiché « 12,9 V / 100 % » sur un véhicule qui ne démarrait pas (259 TU 4987) :
/// l'octet 32-34 (« Power ») ne mesurait rien sur 281 boîtiers NEMS sur 288, d'où
/// le masquage. Le <b>17/09/2026</b>, le fournisseur a indiqué que la tension
/// batterie est l'octet 34-36 (« Batterie »), facteur 40 V / 256 — et que « Power »
/// est à ignorer. Les valeurs chiffrées ci-dessous sont celles relevées sur la
/// flotte TN ces jours-là.</para>
/// </summary>
public class VoltageScaleTests
{
    // ── Échelles ────────────────────────────────────────────────────────────

    [Fact]
    public void NemsBatteryFactor_EstLaPleineEchelle40VSur8Bits()
    {
        VoltageScale.NemsBatteryFactor.Should().BeApproximately(0.156, 0.0005,
            "le fournisseur l'écrit 0,156 ; c'est 40 V / 256");

        // Relevés réels du 17/09 sur les boîtiers qui renseignent le champ.
        (85 * VoltageScale.NemsBatteryFactor).Should().BeApproximately(13.28, 0.01);
        (80 * VoltageScale.NemsBatteryFactor).Should().BeApproximately(12.50, 0.01);
    }

    [Fact]
    public void FactorFor_NeCouvrePlusLesNems_LeurTensionVientDeLOctetBatterie()
    {
        // Le 0,3 sur « Power » est ce qui affichait une fausse assurance.
        VoltageScale.FactorFor("gps_type_1").Should().BeNull();
    }

    [Fact]
    public void FactorFor_Teltonika_ResteAUnDixiemeDeVolt()
    {
        VoltageScale.FactorFor("teltonika").Should().Be(0.1);
        (127 * VoltageScale.FactorFor("teltonika")!.Value).Should().BeApproximately(12.7, 0.01);
    }

    [Theory]
    [InlineData("noron")]
    [InlineData("gt06")]
    [InlineData(null)]
    public void FactorFor_ProtocoleInconnu_RetourneNull_DoncOnNAfficheRien(string? protocol)
    {
        // Inventer une échelle serait pire que ne rien afficher.
        VoltageScale.FactorFor(protocol).Should().BeNull();
    }

    // ── Choix de la source d'affichage ──────────────────────────────────────

    [Fact]
    public void DisplayVolts_Nems_LitLOctetBatterie_PasPower()
    {
        // Opel 250 TU 5217 la nuit du 17/09 : Batterie 85, Power 43.
        VoltageScale.DisplayVolts("gps_type_1", batteryRaw: 85, powerVoltage: 43)
            .Should().BeApproximately(13.28, 0.01);
    }

    [Fact]
    public void DisplayVolts_Nems_SansOctetBatterie_NAfficheRien()
    {
        // 245 TU 536 depuis son firmware C32a : le champ reste à 0 alors que
        // Power vaut toujours 43. Ne rien afficher, plutôt que 12,9 V inventés.
        VoltageScale.DisplayVolts("gps_type_1", batteryRaw: 0, powerVoltage: 43).Should().BeNull();
        VoltageScale.DisplayVolts("gps_type_1", batteryRaw: null, powerVoltage: 43).Should().BeNull();
    }

    [Fact]
    public void DisplayVolts_Nems_OctetDeCap_NAfficheRien()
    {
        // Firmware R00C30d : l'octet « Batterie » recopie le cap (21 = 3,3 V).
        VoltageScale.DisplayVolts("gps_type_1", batteryRaw: 21, powerVoltage: 43).Should().BeNull();
    }

    [Fact]
    public void DisplayVolts_Teltonika_LitToujoursPowerVoltage()
    {
        VoltageScale.DisplayVolts("teltonika", batteryRaw: null, powerVoltage: 127)
            .Should().BeApproximately(12.7, 0.01);
    }

    [Fact]
    public void DisplayVolts_ProtocoleInconnu_NAfficheRien()
    {
        VoltageScale.DisplayVolts("noron", batteryRaw: 85, powerVoltage: 43).Should().BeNull();
    }

    // ── Verdict Teltonika (inchangé : alternateur exigé) ────────────────────

    [Fact]
    public void Teltonika_CapteurSuivantLAlternateur_EstAccepte()
    {
        // 12,7 V au repos → 14,1 V en roulant : comportement électrique normal.
        VoltageScale.EvaluateSensor("teltonika", drivingMedian: 141, restingMedian: 127,
            drivingFrames: 500, restingFrames: 300).Should().BeTrue();
    }

    [Fact]
    public void Teltonika_CapteurPlat_EstRejete()
    {
        // Même valeur au repos et à 20 km/h : aucun alternateur ne fait ça.
        VoltageScale.EvaluateSensor("teltonika", drivingMedian: 127, restingMedian: 127,
            drivingFrames: 500, restingFrames: 300).Should().BeFalse();
    }

    [Fact]
    public void Teltonika_EchelleInvraisemblable_EstRejetee()
    {
        // Le capteur bouge, mais 200 × 0,1 = 20 V au repos : hors système 12 V.
        VoltageScale.EvaluateSensor("teltonika", drivingMedian: 220, restingMedian: 200,
            drivingFrames: 500, restingFrames: 300).Should().BeFalse();
    }

    [Fact]
    public void Nems_NePassePlusParEvaluateSensor()
    {
        // FactorFor ne connaît plus gps_type_1 : ce chemin rend null. Depuis le
        // 25/09/2026, l'audit ne juge d'ailleurs plus du tout les NEMS.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 47, restingMedian: 42,
            drivingFrames: 500, restingFrames: 300).Should().BeNull();
    }

    [Theory]
    [InlineData(10, 300)]   // n'a quasiment pas roulé
    [InlineData(500, 5)]    // jamais vraiment à l'arrêt
    public void Teltonika_PasAssezDeDonnees_ResteIndecis(long drivingFrames, long restingFrames)
    {
        VoltageScale.EvaluateSensor("teltonika", drivingMedian: 141, restingMedian: 127,
            drivingFrames, restingFrames).Should().BeNull();
    }

    // ── Tri de la valeur de l'octet « Batterie » (25/09/2026) ───────────────
    //
    // Flotte NEMS TN, 24 h du 24 au 25/09 : ≈192 000 trames entre 0 et 44 (octet de
    // cap des R00C30d), presque rien entre 45 et 69, la batterie entre 70 et 89.

    [Theory]
    [InlineData("gps_type_1", true)]
    [InlineData("GPS_TYPE_1", true)]
    [InlineData("teltonika", false)]
    [InlineData(null, false)]
    public void IsNems_ReconnaitLeProtocoleSansTenirCompteDeLaCasse(string? protocol, bool attendu)
    {
        VoltageScale.IsNems(protocol).Should().Be(attendu);
    }

    [Theory]
    [InlineData(44)]   // plus haute valeur de l'octet de cap (359° / 8)
    [InlineData(21)]   // cap relevé sur un R00C30d le 24/09
    [InlineData(0)]    // pas de mesure
    [InlineData(93)]   // au-delà du plafond d'alternateur
    [InlineData(255)]
    public void NemsMeaningfulVolts_ValeurSansSens_RetourneNull(int raw)
    {
        VoltageScale.NemsMeaningfulVolts(raw).Should().BeNull();
    }

    [Fact]
    public void NemsMeaningfulVolts_OctetAbsent_RetourneNull()
    {
        VoltageScale.NemsMeaningfulVolts(null).Should().BeNull();
    }

    [Theory]
    [InlineData(45, 7.031)]    // borne basse incluse
    [InlineData(74, 11.5625)]
    [InlineData(80, 12.5)]
    [InlineData(92, 14.375)]   // borne haute incluse, sous 14,4 V
    public void NemsMeaningfulVolts_VraieTension_EstConvertieAu40VSur256(int raw, double volts)
    {
        VoltageScale.NemsMeaningfulVolts(raw).Should().BeApproximately(volts, 0.001);
    }

    [Theory]
    [InlineData(10.9, 0)]
    [InlineData(11.0, 0)]
    [InlineData(11.5625, 31)]
    [InlineData(12.5, 83)]
    [InlineData(12.8, 100)]
    [InlineData(13.9, 100)]
    public void BatteryPercent_EchelleLineaireDe11A12Virgule8V(double volts, int pourcentage)
    {
        VoltageScale.BatteryPercent(volts).Should().Be(pourcentage);
    }
}
