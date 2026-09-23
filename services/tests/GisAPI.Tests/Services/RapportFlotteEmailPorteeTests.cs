using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Portée du rapport de flotte envoyé par e-mail (<see cref="DailyFleetReportService"/>).
///
/// <para>Constat : le service calculait UN contenu sur tout le parc de la société et
/// expédiait le MÊME fichier à chacun. Chez un loueur — dont chaque locataire a un compte
/// restreint à ses propres véhicules — un locataire affecté à 2 véhicules recevait chaque
/// matin l'activité des 307 du parc. Le cloisonnement société est un invariant EF ; le
/// cloisonnement utilisateur est une convention, et elle manquait ici.</para>
///
/// <para>Les tests appellent <c>SendAllAsync</c> / <c>SendWeeklyAsync</c> DE PRODUCTION avec
/// un médiateur et un service de messagerie simulés : ce qui est vérifié, c'est le contenu
/// réellement remis à chaque adresse, pas une copie de la règle de portée.</para>
///
/// <para>Trois états, jamais confondus : <c>null</c> = administrateur (tout le parc, même
/// sans affectation) ; liste non vide = ses véhicules ; liste VIDE = aucun envoi. Le premier
/// test est le GARDE-FOU du cas piège (administrateur sans affectation) : il était vert avant
/// le correctif et doit le rester. Les trois suivants échouent sur le code d'avant.</para>
/// </summary>
public class RapportFlotteEmailPorteeTests
{
    static RapportFlotteEmailPorteeTests()
    {
        // Program.cs pose la licence au démarrage de l'API ; hors hôte il faut la poser ici,
        // sinon QuestPDF refuse de produire le PDF du rapport journalier.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private const int Loueur = 4;
    private const int RoleAdministrateur = 1;
    private const int RoleOperateur = 10;

    private const int Administratrice = 11;   // administratrice, ZÉRO ligne dans UserVehicles
    private const int Locataire = 58;         // opérateur, affecté au seul véhicule 91
    private const int Colocataire = 60;       // opérateur, affecté au MÊME véhicule 91
    private const int Orphelin = 59;          // opérateur, aucune affectation

    private const int Vehicule91 = 91;
    private const int Vehicule171 = 171;
    private const int Vehicule200 = 200;

    /// <summary>Un e-mail réellement expédié : à qui, et avec quel contenu.</summary>
    private sealed record Envoi(string Email, string Corps, byte[] PieceJointe);

    // ── Parc du loueur ─────────────────────────────────────────────────────────

    private static async Task<TestGisDbContext> LoueurAsync()
    {
        var ctx = TestDbContextFactory.Create();

        ctx.Societes.Add(TestDataBuilder.CreateSociete(Loueur));
        ctx.Roles.AddRange(
            new Role { Id = RoleAdministrateur, Name = "Administrateur", SocieteId = Loueur, IsCompanyAdmin = true },
            new Role { Id = RoleOperateur, Name = "Operateur", SocieteId = Loueur, IsCompanyAdmin = false, IsSystemRole = false });

        foreach (var id in new[] { Vehicule91, Vehicule171, Vehicule200 })
            ctx.Vehicles.Add(TestDataBuilder.CreateVehicle(id, Loueur, $"VEH-{id}"));

        ctx.Users.AddRange(
            Destinataire(Administratrice, RoleAdministrateur, "direction@loueur.tn"),
            Destinataire(Locataire, RoleOperateur, "locataire@loueur.tn"),
            Destinataire(Colocataire, RoleOperateur, "colocataire@loueur.tn"),
            Destinataire(Orphelin, RoleOperateur, "orphelin@loueur.tn"));

        ctx.UserVehicles.AddRange(
            new UserVehicle { UserId = Locataire, VehicleId = Vehicule91 },
            new UserVehicle { UserId = Colocataire, VehicleId = Vehicule91 });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static User Destinataire(int id, int roleId, string email)
    {
        var user = TestDataBuilder.CreateUser(id, Loueur, email);
        user.RoleId = roleId;
        user.DailyReportEmailEnabled = true;
        return user;
    }

    // ── Exécution du service de production ─────────────────────────────────────

    /// <summary>
    /// Joue le service et rend ce qui est sorti : les e-mails expédiés, et les périmètres
    /// réellement calculés (un appel au médiateur = un contenu produit).
    /// </summary>
    private static async Task<(List<Envoi> Envois, List<int[]> Perimetres)> ExecuterAsync(
        TestGisDbContext ctx, bool hebdomadaire = false)
    {
        var perimetres = new List<int[]>();
        var mediateur = new Mock<IMediator>();
        mediateur
            .Setup(m => m.Send(It.IsAny<GetDailyActivityReportsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetDailyActivityReportsQuery requete, CancellationToken _) =>
            {
                var ids = requete.VehicleIds ?? Array.Empty<int>();
                perimetres.Add(ids);
                return ids.Select(id => new DailyActivityReportDto
                {
                    VehicleId = id,
                    VehicleName = $"VEH-{id}",
                    Plate = $"{id} TU 4",
                    HasActivity = true,
                    Summary = new DailySummaryDto { TotalDistanceKm = 10, TotalDrivingSeconds = 1800 }
                }).ToList();
            });

        var envois = new List<Envoi>();
        var messagerie = new Mock<IEmailService>();
        messagerie
            .Setup(e => e.SendEmailWithAttachmentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .Callback((string destinataire, string _, string _, string corps,
                       byte[] piece, string _, string _, CancellationToken _, string? _) =>
                envois.Add(new Envoi(destinataire, corps, piece)))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton<IGisDbContext>(ctx);
        services.AddSingleton(mediateur.Object);
        services.AddSingleton(messagerie.Object);

        await using var fournisseur = services.BuildServiceProvider();
        using var portee = fournisseur.CreateScope();

        var service = new DailyFleetReportService(fournisseur, NullLogger<DailyFleetReportService>.Instance);
        if (hebdomadaire)
            await service.SendWeeklyAsync(portee, CancellationToken.None);
        else
            await service.SendAllAsync(portee, CancellationToken.None);

        return (envois, perimetres);
    }

    private static Envoi? Pour(List<Envoi> envois, string email) =>
        envois.SingleOrDefault(e => e.Email == email);

    // ── Rapport journalier ─────────────────────────────────────────────────────

    [Fact]
    public async Task L_administratrice_sans_affectation_recoit_tout_le_parc()
    {
        using var ctx = await LoueurAsync();

        var (envois, _) = await ExecuterAsync(ctx);

        var recu = Pour(envois, "direction@loueur.tn");
        recu.Should().NotBeNull("un administrateur reçoit le rapport même sans une seule ligne dans UserVehicles");
        recu!.Corps.Should().Contain("VEH-91").And.Contain("VEH-171").And.Contain("VEH-200");
        recu.PieceJointe.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Le_locataire_affecte_a_un_vehicule_ne_recoit_que_le_sien()
    {
        using var ctx = await LoueurAsync();

        var (envois, _) = await ExecuterAsync(ctx);

        var recu = Pour(envois, "locataire@loueur.tn");
        recu.Should().NotBeNull();
        recu!.Corps.Should().Contain("VEH-91");
        recu.Corps.Should().NotContain("VEH-171", "les véhicules des autres locataires n'ont rien à faire dans son rapport");
        recu.Corps.Should().NotContain("VEH-200");
    }

    [Fact]
    public async Task Un_destinataire_sans_aucune_affectation_ne_recoit_rien()
    {
        using var ctx = await LoueurAsync();

        var (envois, _) = await ExecuterAsync(ctx);

        Pour(envois, "orphelin@loueur.tn").Should()
            .BeNull("liste de portée VIDE = il ne voit rien : pas de rapport vide, et surtout pas le parc entier");
        envois.Select(e => e.Email).Should().BeEquivalentTo(
            new[] { "direction@loueur.tn", "locataire@loueur.tn", "colocataire@loueur.tn" });
    }

    [Fact]
    public async Task Deux_destinataires_de_meme_perimetre_partagent_un_seul_calcul()
    {
        using var ctx = await LoueurAsync();

        var (envois, perimetres) = await ExecuterAsync(ctx);

        perimetres.Should().HaveCount(2, "deux périmètres distincts : tout le parc, et le seul véhicule 91");
        perimetres.Should().ContainEquivalentOf(new[] { Vehicule91, Vehicule171, Vehicule200 });
        perimetres.Should().ContainEquivalentOf(new[] { Vehicule91 });

        Pour(envois, "colocataire@loueur.tn")!.Corps.Should().Be(
            Pour(envois, "locataire@loueur.tn")!.Corps,
            "même périmètre = un seul contenu produit, expédié aux deux");
    }

    [Fact]
    public async Task La_societe_reste_marquee_comme_traitee_pour_la_journee()
    {
        using var ctx = await LoueurAsync();

        await ExecuterAsync(ctx);

        ctx.ChangeTracker.Clear();
        var societe = await ctx.Societes.FindAsync(Loueur);
        societe!.LastDailyReportSentDate.Should().NotBeNull("le garde-fou anti-doublon doit continuer de s'armer");
    }

    // ── Récap hebdomadaire ─────────────────────────────────────────────────────

    [Fact]
    public async Task Le_recap_hebdomadaire_applique_exactement_la_meme_portee()
    {
        using var ctx = await LoueurAsync();

        var (envois, perimetres) = await ExecuterAsync(ctx, hebdomadaire: true);

        Pour(envois, "orphelin@loueur.tn").Should().BeNull();
        Pour(envois, "direction@loueur.tn")!.Corps.Should().Contain("VEH-171");
        Pour(envois, "locataire@loueur.tn")!.Corps.Should().NotContain("VEH-171");

        // Le récap somme 7 jours : 7 appels par périmètre, et deux périmètres.
        perimetres.Should().HaveCount(14);
        perimetres.Where(p => p.Length == 1).Should().HaveCount(7);
    }
}
