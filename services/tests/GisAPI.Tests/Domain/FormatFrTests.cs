using FluentAssertions;
using GisAPI.Domain.Common;
using Xunit;

namespace GisAPI.Tests.Domain;

/// <summary>
/// Nombres des messages lus par l'utilisateur : {x:N0} suivait la culture du serveur,
/// invariante dans le conteneur, et écrivait « 45,000 km » (lu 45 km) — recette de Karim
/// du 14/10/2026. Espace insécable ordinaire (U+00A0) entre les milliers.
/// </summary>
public class FormatFrTests
{
    private static readonly string Insecable = ((char)0xA0).ToString();

    [Fact]
    public void Milliers_separes_par_une_espace_et_decimales_par_une_virgule()
    {
        FormatFr.N(45000.0, 0).Should().Be($"45{Insecable}000");
        FormatFr.N(1234.5m, 2).Should().Be($"1{Insecable}234,50");
        FormatFr.N(161980, 0).Should().Be($"161{Insecable}980");
        FormatFr.F(12.46, 1).Should().Be("12,5");
    }

    [Fact]
    public void Une_valeur_absente_donne_un_texte_vide()
    {
        FormatFr.N((double?)null, 0).Should().BeEmpty();
        FormatFr.F((double?)null, 1).Should().BeEmpty();
    }
}
