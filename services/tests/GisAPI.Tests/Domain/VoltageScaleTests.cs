using FluentAssertions;
using GisAPI.Domain.Common;
using Xunit;

namespace GisAPI.Tests.Domain;

/// <summary>
/// Règle d'affichage de la batterie. Les valeurs des cas nommés sont celles
/// relevées sur la flotte TN le 14/08/2026, le jour où l'application a affiché
/// « 12,9 V / 100 % » sur un véhicule qui ne démarrait pas.
/// </summary>
public class VoltageScaleTests
{
    [Theory]
    [InlineData("gps_type_1", 0.3)]
    [InlineData("teltonika", 0.1)]
    public void FactorFor_KnownProtocols_ReturnsScale(string protocol, double expected)
    {
        VoltageScale.FactorFor(protocol).Should().Be(expected);
    }

    [Theory]
    [InlineData("noron")]
    [InlineData("gt06")]
    [InlineData(null)]
    public void FactorFor_UnknownProtocol_ReturnsNull_SoNothingIsDisplayed(string? protocol)
    {
        // Inventer une échelle serait pire que ne rien afficher.
        VoltageScale.FactorFor(protocol).Should().BeNull();
    }

    [Fact]
    public void FlatSensor_IsRejected()
    {
        // 259 TU 4987 : 43 au repos comme à 20 km/h. Aucun alternateur ne fait
        // ça — le capteur ne mesure rien, et c'est ce véhicule qui est tombé en
        // panne pendant que l'écran affichait 100 %.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 43, restingMedian: 43,
            drivingFrames: 500, restingFrames: 300).Should().BeFalse();
    }

    [Fact]
    public void SensorFollowingAlternator_IsAccepted()
    {
        // 12,6 V au repos → 14,1 V en roulant : comportement électrique normal.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 47, restingMedian: 42,
            drivingFrames: 500, restingFrames: 300).Should().BeTrue();
    }

    [Fact]
    public void Teltonika_IsScaledAtOneTenth_NotThreeTenths()
    {
        // Médiane 137 = 13,7 V avec le bon facteur. Avec l'ancien 0,3 partagé,
        // ça donnait 41 V, écrêté à 14,4 V — soit « 100 % » en permanence.
        VoltageScale.EvaluateSensor("teltonika", drivingMedian: 141, restingMedian: 127,
            drivingFrames: 500, restingFrames: 300).Should().BeTrue();

        (127 * VoltageScale.FactorFor("teltonika")!.Value).Should().BeApproximately(12.7, 0.01);
    }

    [Fact]
    public void MovingSensorWithImplausibleScale_IsRejected()
    {
        // Le capteur bouge, mais 55 × 0,3 = 16,5 V au repos : hors de tout
        // système 12 V. La jauge calibrée 11,0–12,8 V n'en tirerait rien de bon.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 60, restingMedian: 55,
            drivingFrames: 500, restingFrames: 300).Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 300)]   // n'a quasiment pas roulé
    [InlineData(500, 5)]    // jamais vraiment à l'arrêt
    public void NotEnoughData_StaysUndecided(long drivingFrames, long restingFrames)
    {
        // Indécis, pas « en panne » : un véhicule peu utilisé ne doit pas être
        // déclaré défectueux. En aval, indécis = on n'affiche rien non plus.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 47, restingMedian: 42,
            drivingFrames, restingFrames).Should().BeNull();
    }

    [Fact]
    public void MissingMedians_StayUndecided()
    {
        VoltageScale.EvaluateSensor("gps_type_1", null, null, 500, 300).Should().BeNull();
    }

    [Fact]
    public void DeltaJustUnderThreshold_IsRejected()
    {
        // 2 unités = 0,6 V : trop peu pour un alternateur, qui en ajoute ~1,6.
        VoltageScale.EvaluateSensor("gps_type_1", drivingMedian: 44, restingMedian: 42,
            drivingFrames: 500, restingFrames: 300).Should().BeFalse();
    }
}
