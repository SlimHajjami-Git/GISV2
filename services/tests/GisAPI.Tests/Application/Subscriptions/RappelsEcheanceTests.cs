using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Subscriptions;

/// <summary>
/// Rappels d'échéance au client (préparation du paiement en ligne, 19/10/2026, choix A) :
/// 7 jours avant (2 jours avant la fin d'un essai), le jour de l'échéance, puis à la
/// suspension. Sociétés en libre-service seulement, chaque étape une fois, jamais en
/// rattrapage.
/// </summary>
public class RappelsEcheanceTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);

    private static Societe Societe(double joursAvantEcheance, string code = "plan-basique",
        bool essai = false, bool suspensionAuto = true, string statut = "active")
    {
        var echeance = Now.AddDays(joursAvantEcheance);
        return new Societe
        {
            Id = 30, Name = "Client GPA", IsActive = true,
            SubscriptionType = new SubscriptionType { Id = 3, Code = code },
            SubscriptionTypeId = 3,
            SubscriptionStatus = statut,
            AutoSuspendEnabled = suspensionAuto,
            SubscriptionExpiresAt = echeance,
            // Essai : jamais réglé, échéance à 7 jours du départ. Sinon : réglé, un an.
            SubscriptionStartedAt = essai ? echeance.AddDays(-7) : echeance.AddDays(-365),
            LastPaymentAt = essai ? null : echeance.AddDays(-365),
            Settings = new SocieteSettings { Timezone = "Africa/Tunis" }
        };
    }

    private static SubscriptionReminders.Reminder? Etape(Societe s) =>
        SubscriptionReminders.Due(s, Now, TimeZoneInfo.Utc);

    [Theory]
    [InlineData(6.5)]   // 7 jours restants (jour entamé compté)
    [InlineData(5.5)]   // le lendemain, si le serveur était arrêté
    public void Rappel_sept_jours_avant(double jours) =>
        Etape(Societe(jours))!.Step.Should().Be(SubscriptionReminders.Rappel);

    [Fact]
    public void Le_rappel_donne_la_date_d_echeance_dans_le_fuseau_de_la_societe() =>
        // Échéance le 25/10 à 23 h 30 UTC : déjà le 26 à Tunis (UTC+1), date que voit le client.
        Etape(Societe(6.5625))!.Title.Should().Be("Votre abonnement se termine le 26 octobre 2026");

    [Theory]
    [InlineData(7.5)]
    [InlineData(4.5)]
    [InlineData(20)]
    public void Pas_de_rappel_en_dehors_de_ces_deux_jours(double jours) =>
        Etape(Societe(jours)).Should().BeNull();

    [Fact]
    public void Essai_rappel_deux_jours_avant_la_fin_et_rien_le_jour_de_l_inscription()
    {
        var r = Etape(Societe(1.5, essai: true))!;
        r.Step.Should().Be(SubscriptionReminders.Rappel);
        r.Title.Should().Be("Votre essai gratuit se termine le 20 octobre 2026");

        Etape(Societe(6.5, essai: true)).Should().BeNull();
    }

    [Fact]
    public void Jour_de_l_echeance_annonce_la_date_de_suspension()
    {
        var r = Etape(Societe(-0.1))!;
        r.Step.Should().Be(SubscriptionReminders.Echeance);
        r.Title.Should().Be("Votre abonnement est arrivé à échéance");
        r.Message.Should().Contain("Il vous reste 7 jours pour le renouveler")
            .And.Contain("suspendu le 26 octobre 2026");
    }

    [Fact]
    public void Sans_suspension_automatique_l_echeance_n_annonce_pas_de_suspension()
    {
        var r = Etape(Societe(-0.1, suspensionAuto: false))!;
        r.Step.Should().Be(SubscriptionReminders.Echeance);
        r.Message.Should().NotContain("suspendu");
    }

    [Theory]
    [InlineData(-7.5)]
    [InlineData(-8.5)]
    public void Suspension_a_la_fin_des_sept_jours_de_grace(double jours)
    {
        var r = Etape(Societe(jours))!;
        r.Step.Should().Be(SubscriptionReminders.Suspension);
        r.Title.Should().Be("L'accès de votre société est suspendu");
    }

    [Fact]
    public void Jamais_de_rattrapage_une_suspension_ancienne_ne_declenche_rien() =>
        Etape(Societe(-9.5)).Should().BeNull();

    [Fact]
    public void Client_installe_par_Belive_ou_suspendu_par_Belive_rien()
    {
        Etape(Societe(6.5, code: "plan-standard")).Should().BeNull();
        Etape(Societe(6.5, statut: "suspended")).Should().BeNull();
    }

    // ── Envoi : administrateurs seulement, une fois par étape ──

    [Fact]
    public async Task L_administrateur_recoit_cloche_et_e_mail_une_seule_fois_l_employe_rien()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(new SubscriptionType { Id = 3, Name = "GPA", Code = "plan-basique", IsActive = true });
        var societe = TestDataBuilder.CreateSociete(30, 3);
        societe.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(6.5);
        societe.SubscriptionStartedAt = DateTime.UtcNow.AddDays(-300);
        societe.LastPaymentAt = DateTime.UtcNow.AddDays(-300);
        ctx.Societes.Add(societe);
        ctx.Roles.Add(new Role { Id = 7, Name = "Administrateur", SocieteId = 30, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 8, Name = "Employé", SocieteId = 30, IsCompanyAdmin = false });
        var admin = TestDataBuilder.CreateUser(id: 71, companyId: 30, email: "admin@client.tn");
        admin.RoleId = 7;
        var employe = TestDataBuilder.CreateUser(id: 72, companyId: 30, email: "employe@client.tn");
        employe.RoleId = 8;
        ctx.Users.AddRange(admin, employe);
        ctx.SaveChanges();

        var notifications = new Mock<INotificationService>();
        notifications.Setup(n => n.CreateAndSendAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Returns<int, int, string, string, string, string, string?, int?, string?, Dictionary<string, object>?, CancellationToken>(
                async (companyId, userId, type, title, message, priority, _, _, actionUrl, _, ct) =>
                {
                    var n = new Notification { CompanyId = companyId, UserId = userId, Type = type, Title = title, Message = message, Priority = priority, ActionUrl = actionUrl };
                    ctx.Notifications.Add(n);
                    await ctx.SaveChangesAsync(ct);
                    return n;
                });
        var email = new Mock<IEmailService>();

        var premier = await SubscriptionMonitoringService.SendClientRemindersAsync(ctx, notifications.Object, email.Object, DateTime.UtcNow, CancellationToken.None);
        var second = await SubscriptionMonitoringService.SendClientRemindersAsync(ctx, notifications.Object, email.Object, DateTime.UtcNow, CancellationToken.None);

        premier.Should().Be(1);
        second.Should().Be(0);
        ctx.Notifications.Should().ContainSingle(n => n.UserId == 71 && n.Type == "subscription_reminder_rappel" && n.ActionUrl == "/subscription");
        email.Verify(e => e.SendNotificationEmailAsync("admin@client.tn", It.IsAny<string>(), "subscription_reminder",
            It.Is<string>(t => t.StartsWith("Votre abonnement se termine le")), It.IsAny<string>(), "/subscription",
            It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.SendNotificationEmailAsync("employe@client.tn", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
