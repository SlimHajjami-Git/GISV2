using FluentAssertions;
using GisAPI.Application.Features.Reports.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Classement par type d'intervention. Les neuf descriptions sont celles des
/// réparations réelles de la société 14 (recette du 04/09/2026), toutes sans
/// colonne repair_type : le type doit être déduit et signalé comme tel.
/// </summary>
public class RepairTypeClassifierTests
{
    [Theory]
    [InlineData("Crevaison pneu AVG", RepairTypeClassifier.Pneumatique)]
    [InlineData("Ampoule feu arrière", RepairTypeClassifier.Electrique)]
    [InlineData("Diagnostic moteur/injection", RepairTypeClassifier.Mecanique)]
    [InlineData("Sonde lambda", RepairTypeClassifier.Electrique)]
    [InlineData("Balais essuie-glaces AV", RepairTypeClassifier.Carrosserie)]
    [InlineData("Essuie-glaces AV", RepairTypeClassifier.Carrosserie)]
    [InlineData("Crevaison pneu ARD", RepairTypeClassifier.Pneumatique)]
    [InlineData("Ampoule feu de croisement", RepairTypeClassifier.Electrique)]
    [InlineData("Plaquettes de frein AV", RepairTypeClassifier.Freinage)]
    public void Real_descriptions_of_company_14_are_inferred(string description, string expected)
    {
        var (type, inferred) = RepairTypeClassifier.Classify(null, description);

        type.Should().Be(expected);
        inferred.Should().BeTrue();
    }

    [Fact]
    public void Explicit_repair_type_wins_over_the_description()
    {
        var (type, inferred) = RepairTypeClassifier.Classify("Freinage", "Crevaison pneu AVG");

        type.Should().Be(RepairTypeClassifier.Freinage);
        inferred.Should().BeFalse();
    }

    [Theory]
    [InlineData("Révision annuelle")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unknown_or_empty_description_falls_back_to_autre(string? description)
    {
        var (type, inferred) = RepairTypeClassifier.Classify(null, description);

        type.Should().Be(RepairTypeClassifier.Autre);
        inferred.Should().BeTrue();
    }

    [Theory]
    [InlineData("Étrier de frein arrière", RepairTypeClassifier.Freinage)]
    [InlineData("ÉCHAPPEMENT percé", RepairTypeClassifier.Mecanique)]
    [InlineData("Rétroviseur cassé", RepairTypeClassifier.Carrosserie)]
    [InlineData("Remplacement feu", RepairTypeClassifier.Electrique)]
    public void Accents_and_case_do_not_matter(string description, string expected)
    {
        RepairTypeClassifier.Classify(null, description).Type.Should().Be(expected);
    }

    [Fact]
    public void Blank_explicit_type_is_treated_as_missing()
    {
        var (type, inferred) = RepairTypeClassifier.Classify("  ", "Plaquettes de frein AV");

        type.Should().Be(RepairTypeClassifier.Freinage);
        inferred.Should().BeTrue();
    }

    [Theory]
    [InlineData(RepairTypeClassifier.Electrique, "Électrique")]
    [InlineData(RepairTypeClassifier.Mecanique, "Mécanique")]
    [InlineData(RepairTypeClassifier.Freinage, "Freinage")]
    [InlineData(RepairTypeClassifier.Pneumatique, "Pneumatique")]
    [InlineData(RepairTypeClassifier.Carrosserie, "Carrosserie")]
    [InlineData(RepairTypeClassifier.Autre, "Autres")]
    public void Labels_are_the_french_ones_of_the_contract(string type, string label)
    {
        RepairTypeClassifier.Label(type).Should().Be(label);
    }
}
