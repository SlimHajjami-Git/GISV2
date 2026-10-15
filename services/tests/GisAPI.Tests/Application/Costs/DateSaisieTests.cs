using FluentAssertions;
using GisAPI.Application.Common;
using Xunit;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Dépense et plein datés de juin 2027 enregistrés sans un mot (campagne de tests GPA du
/// 06/10/2026, erreur n°13) : la date d'un fait passé ne peut pas être dans le futur.
/// </summary>
public class DateSaisieTests
{
    private static readonly DateTime Maintenant = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Une_date_future_est_refusee_avec_un_message_clair()
    {
        DateSaisie.ErreurDateFuture(new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc), "de la dépense", Maintenant)
            .Should().Be("La date de la dépense ne peut pas être dans le futur : au plus tard aujourd'hui (15/10/2026).");
    }

    [Theory]
    [InlineData(2026, 10, 15)]   // aujourd'hui
    [InlineData(2026, 10, 16)]   // tolérance d'un jour (fuseau en avance sur UTC, peu après minuit)
    [InlineData(2026, 9, 1)]     // passé
    public void Aujourd_hui_le_passe_et_la_tolerance_d_un_jour_sont_acceptes(int annee, int mois, int jour)
    {
        DateSaisie.ErreurDateFuture(new DateTime(annee, mois, jour, 0, 0, 0, DateTimeKind.Utc), "du plein", Maintenant)
            .Should().BeNull();
    }

    [Fact]
    public void Apres_demain_est_refuse()
    {
        DateSaisie.ErreurDateFuture(new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc), "du plein", Maintenant)
            .Should().StartWith("La date du plein ne peut pas être dans le futur");
    }
}
