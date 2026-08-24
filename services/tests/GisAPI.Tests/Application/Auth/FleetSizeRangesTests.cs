using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.Register;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Tranches de taille de parc recueillies à l'inscription. Donnée commerciale :
/// elle ne doit jamais faire échouer une création de compte, mais on ne stocke
/// pas pour autant une valeur inventée.
/// </summary>
public class FleetSizeRangesTests
{
    [Theory]
    [InlineData("1-5")]
    [InlineData("6-20")]
    [InlineData("21-50")]
    [InlineData("51-100")]
    [InlineData("100+")]
    public void LesCinqTranchesDuCahierDesCharges_SontAcceptees(string tranche)
    {
        FleetSizeRanges.IsValid(tranche).Should().BeTrue();
    }

    [Fact]
    public void AbsenceDeTranche_EstValide()
    {
        // Le champ est facultatif côté API : un client qui ne l'envoie pas —
        // l'application mobile par exemple — doit continuer de fonctionner.
        FleetSizeRanges.IsValid(null).Should().BeTrue();
    }

    [Theory]
    [InlineData("2-7")]        // tranche inventée
    [InlineData("1–5")]        // tiret cadratin au lieu du trait d'union
    [InlineData("100")]        // le « + » manque
    [InlineData("")]           // chaîne vide, distincte de null
    [InlineData("beaucoup")]
    public void UneTrancheInconnue_EstRejetee(string tranche)
    {
        FleetSizeRanges.IsValid(tranche).Should().BeFalse();
    }

    [Fact]
    public void LesCodesExposesCorrespondentAuxValeursAttendues()
    {
        // Le formulaire envoie ces chaînes telles quelles : si un code change
        // ici sans changer là-bas, l'inscription stockerait null en silence.
        FleetSizeRanges.OneToFive.Should().Be("1-5");
        FleetSizeRanges.SixToTwenty.Should().Be("6-20");
        FleetSizeRanges.TwentyOneToFifty.Should().Be("21-50");
        FleetSizeRanges.FiftyOneToHundred.Should().Be("51-100");
        FleetSizeRanges.HundredPlus.Should().Be("100+");
    }
}
