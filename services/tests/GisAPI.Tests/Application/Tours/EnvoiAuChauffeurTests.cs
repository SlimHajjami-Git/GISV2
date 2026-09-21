using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// « Envoyer au chauffeur » (POST /api/tours/{id}/send, lot 1, migration 051) : la
/// tournée part sur le téléphone du compte relié à la fiche, et le gestionnaire sait
/// si le téléphone a été joint. Un chauffeur sans compte, ou inactif, ne reçoit rien.
/// Changer de chauffeur annule l'envoi.
/// </summary>
public class EnvoiAuChauffeurTests
{
    private const int CompanyId = 7;
    private const int Gestionnaire = 1;
    private const int CompteChauffeur = 61;
    private const int Fiche = 31;
    private const int FicheSansCompte = 32;
    private static readonly DateTime Depart = new(2030, 9, 21, 7, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(Gestionnaire);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static (ToursController Controller, Mock<INotificationService> Notifs) Controleur(TestGisDbContext ctx, string push = "delivered_to_fcm")
    {
        var notifs = new Mock<INotificationService>();
        notifs.Setup(n => n.CreateAndSendWithPushAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new Notification { Id = 1 }, push));

        var controller = new ToursController(ctx, Admin(), new Mock<IValhallaService>().Object,
            new Mock<IRedisCacheService>().Object, notifs.Object, NullLogger<ToursController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, Gestionnaire.ToString())
                    }, "test"))
                }
            }
        };
        return (controller, notifs);
    }

    private static async Task<TestGisDbContext> ParcAsync(int? driverId = Fiche, string statutCompte = "active")
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId });
        var compte = TestDataBuilder.CreateUser(id: CompteChauffeur, companyId: CompanyId, email: "c@test.com");
        compte.AccountType = UserAccountTypes.Driver;
        compte.Status = statutCompte;
        ctx.Users.Add(compte);
        ctx.Vehicles.Add(new Vehicle { Id = 5, Name = "Camion 5", Plate = "100 TU 5", CompanyId = CompanyId });
        ctx.Drivers.Add(new Driver { Id = Fiche, CompanyId = CompanyId, UserId = CompteChauffeur, FirstName = "Ali", LastName = "B" });
        ctx.Drivers.Add(new Driver { Id = FicheSansCompte, CompanyId = CompanyId, FirstName = "Sami", LastName = "C" });
        ctx.Tours.Add(new Tour { Id = 10, CompanyId = CompanyId, Name = "Livraison", VehicleId = 5, DriverId = driverId, Status = "planned", ScheduledStartTime = Depart });
        ctx.TourWaypoints.AddRange(
            new TourWaypoint { Id = 101, TourId = 10, SequenceOrder = 0, Type = "origin", Latitude = 36.8, Longitude = 10.18 },
            new TourWaypoint { Id = 102, TourId = 10, SequenceOrder = 1, Type = "destination", Latitude = 35.8, Longitude = 10.63 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task L_envoi_enregistre_qui_et_quand_et_pousse_sur_le_compte_du_chauffeur()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);

        var ok = (await c.SendToDriver(10)).Should().BeOfType<OkObjectResult>().Subject;
        var corps = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        corps.Should().Contain("\"push\":\"delivered_to_fcm\"").And.Contain("\"resent\":false");

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10);
        tour.SentAt.Should().NotBeNull();
        tour.SentByUserId.Should().Be(Gestionnaire);
        notifs.Verify(n => n.CreateAndSendWithPushAsync(CompanyId, CompteChauffeur, "tour_assigned",
            It.Is<string>(s => s.StartsWith("Nouvelle tournée")), It.IsAny<string>(), "high", "tour", 10, "/tournees/10",
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Renvoi : même chemin, titre « mise à jour », resent = true.
        var re = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)await c.SendToDriver(10)).Value);
        re.Should().Contain("\"resent\":true");
    }

    [Fact]
    public async Task Un_chauffeur_sans_compte_application_ne_peut_pas_recevoir_la_tournee()
    {
        using var ctx = await ParcAsync(driverId: FicheSansCompte);
        var (c, notifs) = Controleur(ctx);

        var r = (await c.SendToDriver(10)).Should().BeOfType<BadRequestObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(r.Value).Should().Contain("DRIVER_NO_APP_ACCOUNT");
        notifs.VerifyNoOtherCalls();
        ctx.ChangeTracker.Clear();
        (await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10)).SentAt.Should().BeNull();
    }

    [Fact]
    public async Task Un_compte_chauffeur_desactive_compte_comme_absent()
    {
        using var ctx = await ParcAsync(statutCompte: "inactive");
        var (c, _) = Controleur(ctx);

        (await c.SendToDriver(10)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Sans_chauffeur_l_envoi_est_refuse()
    {
        using var ctx = await ParcAsync(driverId: null);
        var (c, _) = Controleur(ctx);

        (await c.SendToDriver(10)).Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Changer_de_chauffeur_annule_l_envoi_precedent()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx);
        await c.SendToDriver(10);
        ctx.ChangeTracker.Clear();

        var r = await c.UpdateTour(10, new UpdateTourRequest { DriverId = FicheSansCompte });

        r.Should().BeOfType<OkObjectResult>();
        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10);
        (tour.DriverId, tour.SentAt, tour.SentByUserId).Should().Be((FicheSansCompte, (DateTime?)null, (int?)null));
    }

    // ── Relecture du 21/09/2026 (F10) : l'ancien chauffeur est prévenu ─────────

    private static void VerifierRetrait(Mock<INotificationService> notifs, Times fois) =>
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, CompteChauffeur, "tour_cancelled",
            It.Is<string>(s => s.StartsWith("Tournée retirée")), It.IsAny<string>(), "high", "tour", 10, "/tournees/10",
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), fois);

    [Fact]
    public async Task Changer_le_chauffeur_d_une_tournee_envoyee_previent_l_ancien()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);
        await c.SendToDriver(10);
        ctx.ChangeTracker.Clear();

        (await c.UpdateTour(10, new UpdateTourRequest { DriverId = FicheSansCompte })).Should().BeOfType<OkObjectResult>();

        VerifierRetrait(notifs, Times.Once());
    }

    [Fact]
    public async Task Retirer_le_chauffeur_d_une_tournee_envoyee_le_previent_aussi()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);
        await c.SendToDriver(10);
        ctx.ChangeTracker.Clear();

        (await c.UpdateTour(10, new UpdateTourRequest { DriverId = null })).Should().BeOfType<OkObjectResult>();

        VerifierRetrait(notifs, Times.Once());
    }

    [Fact]
    public async Task Modifier_une_tournee_sans_changer_de_chauffeur_ou_jamais_envoyee_ne_previent_personne()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);

        // Jamais envoyée : le chauffeur n'en sait rien, il n'y a rien à lui retirer.
        await c.UpdateTour(10, new UpdateTourRequest { DriverId = FicheSansCompte });
        ctx.ChangeTracker.Clear();
        await c.UpdateTour(10, new UpdateTourRequest { DriverId = Fiche });
        ctx.ChangeTracker.Clear();
        // Envoyée, puis simple changement de nom.
        await c.SendToDriver(10);
        ctx.ChangeTracker.Clear();
        await c.UpdateTour(10, new UpdateTourRequest { Name = "Livraison bis", DriverId = Fiche });

        VerifierRetrait(notifs, Times.Never());
    }

    [Fact]
    public async Task Supprimer_une_tournee_envoyee_previent_le_chauffeur_avant_la_suppression()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);
        await c.SendToDriver(10);
        ctx.ChangeTracker.Clear();

        (await c.DeleteTour(10)).Should().BeOfType<NoContentResult>();

        VerifierRetrait(notifs, Times.Once());
        (await ctx.Tours.AsNoTracking().AnyAsync(t => t.Id == 10)).Should().BeFalse();
    }

    [Fact]
    public async Task Supprimer_une_tournee_jamais_envoyee_ne_previent_personne()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx);

        (await c.DeleteTour(10)).Should().BeOfType<NoContentResult>();

        VerifierRetrait(notifs, Times.Never());
    }

    [Fact]
    public async Task Le_push_non_delivre_est_dit_au_gestionnaire_sans_annuler_l_envoi()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, push: "no_device");

        var corps = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)await c.SendToDriver(10)).Value);

        corps.Should().Contain("\"push\":\"no_device\"");
        ctx.ChangeTracker.Clear();
        (await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10)).SentAt.Should().NotBeNull("la tournée est envoyée : l'app la lira à l'ouverture");
    }

    // ── R11c : démarrage manuel d'une tournée envoyée à un chauffeur déjà en tournée ──

    /// <summary>La tournée 10 est envoyée ; le même chauffeur est sur la tournée 12, partie il y a <paramref name="partieIlYaHeures"/> h.</summary>
    private static async Task<TestGisDbContext> ChauffeurEnTourneeAsync(double partieIlYaHeures, bool tourneeEnvoyee = true)
    {
        var ctx = await ParcAsync();
        var now = DateTime.UtcNow;
        var tour = await ctx.Tours.SingleAsync(t => t.Id == 10);
        tour.SentAt = tourneeEnvoyee ? now.AddHours(-2) : null;
        ctx.Tours.Add(new Tour
        {
            Id = 12, CompanyId = CompanyId, Name = "Tournée du matin", VehicleId = 5, DriverId = Fiche, Status = "in_progress",
            ScheduledStartTime = now.AddHours(-partieIlYaHeures), ActualStartTime = now.AddHours(-partieIlYaHeures),
            SentAt = now.AddHours(-partieIlYaHeures - 1)
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task Demarrer_une_tournee_envoyee_a_un_chauffeur_deja_en_tournee_est_refuse_avec_le_nom_de_l_autre()
    {
        // Démarrée d'ici, elle devenait la tournée en cours la plus récente du chauffeur : son
        // téléphone y basculait et la trace de la tournée qu'il fait vraiment partait dessus.
        using var ctx = await ChauffeurEnTourneeAsync(partieIlYaHeures: 3);
        var (c, _) = Controleur(ctx);

        var r = await c.StartTour(10);

        var corps = System.Text.Json.JsonSerializer.SerializeToElement(r.Should().BeOfType<ConflictObjectResult>().Subject.Value);
        corps.GetProperty("code").GetString().Should().Be(ToursController.DriverBusyCode);
        corps.GetProperty("otherTourName").GetString().Should().Be("Tournée du matin");
        corps.GetProperty("message").GetString().Should().Contain("Tournée du matin");
        ctx.ChangeTracker.Clear();
        (await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10)).Status.Should().Be("planned");
    }

    [Fact]
    public async Task Une_autre_tournee_partie_il_y_a_plus_de_12_h_ne_bloque_pas_le_demarrage()
    {
        using var ctx = await ChauffeurEnTourneeAsync(partieIlYaHeures: 13);
        var (c, _) = Controleur(ctx);

        (await c.StartTour(10)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Une_tournee_jamais_envoyee_se_demarre_comme_avant()
    {
        using var ctx = await ChauffeurEnTourneeAsync(partieIlYaHeures: 3, tourneeEnvoyee: false);
        var (c, _) = Controleur(ctx);

        (await c.StartTour(10)).Should().BeOfType<OkObjectResult>("le téléphone ne suit pas une tournée non envoyée");
    }
}
