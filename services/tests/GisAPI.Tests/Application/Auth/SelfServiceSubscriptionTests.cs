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
    public void L_offre_en_libre_service_voit_l_ecran_avec_ou_sans_paiement()
    {
        // Les comptes issus de l'inscription libre atterrissent tous sur ce plan :
        // la période d'essai est couverte par le même critère.
        LoginCommandHandler.IsSelfServiceSubscription(Societe("plan-basique", null))
            .Should().BeTrue();
        LoginCommandHandler.IsSelfServiceSubscription(Societe("plan-basique", new DateTime(2026, 6, 1)))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("plan-pro")]
    [InlineData("plan-standard")]
    [InlineData("plan-premium")]
    [InlineData("gpa")]
    public void Un_client_installe_ne_voit_pas_l_ecran_meme_sans_paiement_enregistre(string planCode)
    {
        // LE cas qui a fait échouer la première règle : les règlements des clients
        // installés se font HORS application, last_payment_at est vide pour toutes
        // les sociétés de production. « Jamais réglé » ne veut donc pas dire
        // « en essai » — sur ce critère, tout le monde voyait l'écran de paiement.
        LoginCommandHandler.IsSelfServiceSubscription(Societe(planCode, null))
            .Should().BeFalse();
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
