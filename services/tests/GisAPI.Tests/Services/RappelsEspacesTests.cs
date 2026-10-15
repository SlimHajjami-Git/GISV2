using FluentAssertions;
using GisAPI.Services;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Rappels d'échéance, de permis et d'entretien : l'anti-doublon valait l'intervalle du contrôle
/// (6 h), le même rappel repartait donc 4 fois par jour, e-mails compris (10 983 rappels
/// d'entretien chez HERTZ en 7 jours). Décision de Karim du 15/10/2026 : une fois tous les deux
/// jours (campagne de tests GPA du 06/10/2026, erreur n°10).
/// </summary>
public class RappelsEspacesTests
{
    private static readonly DateTime Maintenant = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Un_rappel_n_est_renvoye_qu_au_bout_de_48_heures()
    {
        PredictiveAlertService.RappelToutesLesHeures.Should().Be(48);

        var seuil = PredictiveAlertService.SeuilDeRenvoi(Maintenant);

        seuil.Should().Be(Maintenant.AddHours(-48));
        // Envoyé il y a 6 h ou 47 h : encore « récent », pas de renvoi au passage suivant.
        Maintenant.AddHours(-6).Should().BeOnOrAfter(seuil);
        Maintenant.AddHours(-47).Should().BeOnOrAfter(seuil);
        // Envoyé il y a 49 h : plus récent, le rappel repart.
        Maintenant.AddHours(-49).Should().BeBefore(seuil);
    }

    [Fact]
    public void Une_fenetre_propre_comme_l_anomalie_carburant_reste_respectee()
    {
        PredictiveAlertService.SeuilDeRenvoi(Maintenant, TimeSpan.FromDays(7))
            .Should().Be(Maintenant.AddDays(-7));
    }
}
