using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Qui a le droit de voir l'écran Abonnement / paiement.
///
/// La règle décide de ce qu'un client voit dans son menu : elle mérite d'être
/// éprouvée ailleurs que sur les données de production, d'autant qu'aucune société
/// du serveur de test n'a jamais réglé — l'exclusion n'y serait donc pas observable.
/// </summary>
public class SelfServiceSubscriptionTests
{
    private static Societe Societe(string planCode, DateTime? lastPayment) => new()
    {
        Id = 1,
        Name = "Test",
        LastPaymentAt = lastPayment,
        SubscriptionType = new SubscriptionType { Id = 1, Code = planCode, Name = planCode }
    };

    [Fact]
    public void Une_societe_qui_n_a_jamais_regle_est_en_essai()
    {
        // C'est le cas de tout compte issu de l'inscription libre : c'est
        // précisément à lui qu'il faut montrer les offres.
        LoginCommandHandler.IsSelfServiceSubscription(Societe("plan-pro", null))
            .Should().BeTrue();
    }

    [Fact]
    public void L_offre_de_gestion_de_parc_reste_en_libre_service_apres_paiement()
    {
        LoginCommandHandler.IsSelfServiceSubscription(Societe("plan-basique", new DateTime(2026, 6, 1)))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("plan-pro")]
    [InlineData("plan-standard")]
    [InlineData("plan-premium")]
    [InlineData("gpa")]
    public void Un_client_installe_qui_a_regle_ne_voit_pas_l_ecran(string planCode)
    {
        // Abonnement négocié puis facturé à la main : une grille tarifaire et un
        // bouton de paiement inactif ne feraient que susciter des questions.
        LoginCommandHandler.IsSelfServiceSubscription(Societe(planCode, new DateTime(2026, 6, 1)))
            .Should().BeFalse();
    }

    [Fact]
    public void Une_societe_absente_ne_donne_aucun_droit()
    {
        LoginCommandHandler.IsSelfServiceSubscription(null).Should().BeFalse();
    }

    [Fact]
    public void Une_societe_qui_a_regle_mais_sans_plan_connu_ne_voit_pas_l_ecran()
    {
        var societe = Societe("plan-pro", new DateTime(2026, 6, 1));
        societe.SubscriptionType = null;

        LoginCommandHandler.IsSelfServiceSubscription(societe).Should().BeFalse();
    }
}
