using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Application.Features.Reports.Queries.GetMileagePeriodReport;
using GisAPI.Application.Features.Reports.Queries.GetMileageReport;
using GisAPI.Application.Features.Reports.Queries.GetStopsReport;
using GisAPI.Application.Features.Reports.Queries.GetTripsReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using GpsController = global::GisAPI.Controllers.GpsController;
using ReportsController = global::GisAPI.Controllers.ReportsController;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Portée véhicules des RAPPORTS — incident de confidentialité HERTZ (société 4).
///
/// HERTZ est un loueur : ses 307 véhicules sont loués à des clients distincts, chacun
/// avec un compte restreint à ses propres véhicules (table user_vehicles). Kap Pharma
/// (utilisateur 58, rôle « Opérateur », affecté aux véhicules 91 et 171) voyait pourtant
/// les données des 305 autres.
///
/// DEUX défauts, tous deux couverts ici :
///
///  1. Les rapports « tous véhicules » partaient du parc de la société et n'appliquaient
///     de filtre que <c>if (VehicleIds != null &amp;&amp; VehicleIds.Length > 0)</c>. Or le front
///     n'envoie PAS ce paramètre quand aucun véhicule n'est sélectionné
///     (<c>api.service.ts</c> : <c>if (vehicleIds?.length)</c>) — c'est mot pour mot la
///     réclamation « les rapports aussi, quand on ne sélectionne aucun véhicule ».
///     La portée doit s'appliquer AVANT le filtre optionnel, qui l'INTERSECTE ensuite.
///
///  2. IDOR : les routes « un véhicule » ne vérifiaient que la société, si bien qu'il
///     suffisait de changer l'identifiant dans l'URL. Le refus reste un 404 identique à
///     celui d'un véhicule inexistant : on ne révèle pas qu'il existe (VehicleScope.cs).
///
/// Les TROIS états de la portée (<c>VehicleScope</c>) ne doivent jamais être confondus :
///   • <c>null</c>      → administrateur, AUCUN filtre (tout le parc) ;
///   • liste non vide   → ses véhicules ;
///   • liste VIDE       → non-administrateur sans affectation, il ne voit RIEN.
/// Le cas piège est l'administrateur SANS aucune ligne dans user_vehicles (utilisateur 11
/// de HERTZ) : il doit continuer à tout voir. D'où un test jumeau « admin » partout.
/// </summary>
public class ReportsVehicleScopeTests
{
    private const int CompanyId = 1;
    private const int AutreSociete = 2;

    /// <summary>Le locataire restreint (Kap Pharma, utilisateur 58) : un seul véhicule affecté.</summary>
    private const int LocataireUserId = 58;

    /// <summary>L'administrateur de la société SANS aucune affectation (utilisateur 11 de HERTZ).</summary>
    private const int AdminUserId = 11;

    private static readonly DateTime Debut = new(2026, 9, 1);
    private static readonly DateTime Fin = new(2026, 9, 30);

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Locataire() => Tenant(LocataireUserId, "user");
    private static ICurrentTenantService AdminSansAffectation() => Tenant(AdminUserId, "company_admin");

    // ───────────────── Jeu de données ─────────────────

    /// <summary>
    /// Société 1 : véhicules 1 (loué au locataire), 2 et 3 (loués à d'autres).
    /// Plus un véhicule 9 d'une autre société, qui ne doit jamais apparaître.
    ///
    /// <paramref name="avecBoitier"/> : les rapports trajets / arrêts / activité ne
    /// retiennent que les véhicules équipés d'un boîtier ; le rapport kilométrage se
    /// teste au contraire sur des véhicules SANS boîtier (offre GPA), car le calcul
    /// GPS passe par une agrégation SQL PostGIS que SQLite ne sait pas exécuter — et
    /// c'est précisément ce second passage « véhicules sans boîtier » qui avait lui
    /// aussi oublié la portée.
    /// </summary>
    private static async Task SeedAsync(TestGisDbContext ctx, bool avecBoitier)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId, GpsDeviceId = avecBoitier ? 11 : null },
            new Vehicle { Id = 2, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId, GpsDeviceId = avecBoitier ? 12 : null },
            new Vehicle { Id = 3, Name = "Loué Djerba", Plate = "333 TU 3", CompanyId = CompanyId, GpsDeviceId = avecBoitier ? 13 : null },
            new Vehicle { Id = 9, Name = "Autre société", Plate = "999 TU 9", CompanyId = AutreSociete, GpsDeviceId = avecBoitier ? 19 : null });

        // Seul le véhicule 1 est affecté au locataire. L'administrateur, lui, n'a
        // AUCUNE ligne ici : c'est le cas piège à ne pas casser.
        ctx.UserVehicles.Add(new UserVehicle { UserId = LocataireUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Les rapports « tous véhicules » délèguent le calcul d'UN véhicule au médiateur.
    /// On renvoie un rapport vide qui ne porte que l'identifiant : seule la LISTE des
    /// véhicules retenus est en cause ici.
    /// </summary>
    private static IMediator MediateurEcho()
    {
        var m = new Mock<IMediator>();
        m.Setup(x => x.Send(It.IsAny<GetTripsReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetTripsReportQuery q, CancellationToken _) => new TripsReportDto { VehicleId = q.VehicleId });
        m.Setup(x => x.Send(It.IsAny<GetStopsReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetStopsReportQuery q, CancellationToken _) => new StopsReportDto { VehicleId = q.VehicleId });
        m.Setup(x => x.Send(It.IsAny<GetDailyActivityReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetDailyActivityReportQuery q, CancellationToken _) => new DailyActivityReportDto { VehicleId = q.VehicleId });
        return m.Object;
    }

    /// <summary>Identifiants des véhicules rendus par le rapport « tous véhicules » demandé.</summary>
    private static async Task<List<int>> VehiculesDuRapportAsync(string rapport, ICurrentTenantService tenant, int[]? vehicleIds)
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx, avecBoitier: rapport != "kilometrage");
        var mediateur = MediateurEcho();
        var ct = CancellationToken.None;

        var ids = rapport switch
        {
            "trajets" => (await new GetTripsReportAllVehiclesQueryHandler(ctx, mediateur, tenant)
                .Handle(new GetTripsReportAllVehiclesQuery(Debut, Fin, vehicleIds), ct))
                .Select(r => r.VehicleId),

            "arrets" => (await new GetStopsReportAllVehiclesQueryHandler(ctx, mediateur, tenant)
                .Handle(new GetStopsReportAllVehiclesQuery(Debut, Fin, vehicleIds), ct))
                .Select(r => r.VehicleId),

            "kilometrage" => (await new GetMileageReportsQueryHandler(ctx, mediateur, tenant)
                .Handle(new GetMileageReportsQuery(Debut, Fin, vehicleIds), ct))
                .Select(r => r.VehicleId),

            "activite" => (await new GetDailyActivityReportsQueryHandler(ctx, mediateur, tenant)
                .Handle(new GetDailyActivityReportsQuery(Debut, vehicleIds), ct))
                .Select(r => r.VehicleId),

            _ => throw new ArgumentOutOfRangeException(nameof(rapport), rapport, "rapport inconnu")
        };

        return ids.OrderBy(i => i).ToList();
    }

    // ───────────────── B1 : la portée manquante ─────────────────

    [Theory]
    [InlineData("trajets")]
    [InlineData("arrets")]
    [InlineData("kilometrage")]
    [InlineData("activite")]
    public async Task Sans_aucun_vehicule_selectionne_le_locataire_ne_recoit_que_le_sien(string rapport)
    {
        // Exactement le cas de la réclamation : le front n'envoie pas vehicleIds.
        (await VehiculesDuRapportAsync(rapport, Locataire(), null))
            .Should().Equal(new[] { 1 },
                "sans sélection, la portée de l'appelant doit s'appliquer — et non le parc entier de la société");
    }

    [Theory]
    [InlineData("trajets")]
    [InlineData("arrets")]
    [InlineData("kilometrage")]
    [InlineData("activite")]
    public async Task Une_selection_portant_sur_le_vehicule_d_un_autre_ne_rend_rien(string rapport)
    {
        // Le véhicule 2 existe bien dans la société, mais il n'est pas affecté au
        // locataire : la sélection INTERSECTE la portée, elle ne la remplace pas.
        (await VehiculesDuRapportAsync(rapport, Locataire(), new[] { 2 }))
            .Should().BeEmpty("le filtre de l'écran ne doit jamais élargir la portée");
    }

    [Theory]
    [InlineData("trajets")]
    [InlineData("arrets")]
    [InlineData("kilometrage")]
    [InlineData("activite")]
    public async Task Le_locataire_garde_ses_propres_vehicules_quand_il_les_selectionne(string rapport)
    {
        (await VehiculesDuRapportAsync(rapport, Locataire(), new[] { 1, 2 }))
            .Should().Equal(new[] { 1 }, "l'intersection garde ce qui lui appartient");
    }

    [Theory]
    [InlineData("trajets")]
    [InlineData("arrets")]
    [InlineData("kilometrage")]
    [InlineData("activite")]
    public async Task Un_administrateur_sans_aucune_affectation_voit_tout_le_parc(string rapport)
    {
        // TEST JUMEAU : l'utilisateur 11 de HERTZ est administrateur et n'a aucune
        // ligne dans user_vehicles. Portée null = aucun filtre, surtout pas liste vide.
        (await VehiculesDuRapportAsync(rapport, AdminSansAffectation(), null))
            .Should().Equal(new[] { 1, 2, 3 },
                "un administrateur voit tout le parc de sa société, jamais restreint par les affectations");
    }

    // ───────────────── B2 : l'IDOR des routes « un véhicule » ─────────────────

    /// <summary>Vrai GisDbContext en mémoire : ces routes vérifient le véhicule DANS le contrôleur.</summary>
    private sealed class ContexteControleur : GisDbContext
    {
        public ContexteControleur()
            : base(new DbContextOptionsBuilder<GisDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
                    .Options,
                TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        // Colonnes propres à PostgreSQL (jsonb, tableaux…) sans équivalent en mémoire.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    private static async Task<ContexteControleur> ContexteControleurSeedeAsync()
    {
        var ctx = new ContexteControleur();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId, GpsDeviceId = 11 },
            new Vehicle { Id = 2, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId, GpsDeviceId = 12 });
        ctx.UserVehicles.Add(new UserVehicle { UserId = LocataireUserId, VehicleId = 1 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    /// <summary>Rapports d'UN véhicule : le calcul n'est pas en cause, seul l'accès l'est.</summary>
    private static IMediator MediateurRapportsVides()
    {
        var m = new Mock<IMediator>();
        m.Setup(x => x.Send(It.IsAny<GetDailyActivityReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyActivityReportDto)null!);
        m.Setup(x => x.Send(It.IsAny<GetMileageReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MileageReportDto)null!);
        m.Setup(x => x.Send(It.IsAny<GetStopsReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StopsReportDto)null!);
        m.Setup(x => x.Send(It.IsAny<GetTripsReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TripsReportDto)null!);
        m.Setup(x => x.Send(It.IsAny<GetMileagePeriodReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MileagePeriodReportDto)null!);
        m.Setup(x => x.Send(It.IsAny<GetVehicleCostEvolutionQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((VehicleCostEvolutionDto)null!);
        return m.Object;
    }

    private static ControllerContext ContexteHttp() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", CompanyId.ToString()) }))
        }
    };

    private static ReportsController Rapports(ContexteControleur ctx, ICurrentTenantService tenant) =>
        new(ctx, MediateurRapportsVides(), null!, tenant) { ControllerContext = ContexteHttp() };

    /// <summary>Les six routes « un véhicule » du contrôleur des rapports, pour l'identifiant donné.</summary>
    private static async Task<ActionResult?[]> ToutesLesRoutesAsync(ReportsController c, int vehicleId) => new[]
    {
        (await c.GetDailyReport(vehicleId)).Result,
        (await c.GetMileageReport(vehicleId)).Result,
        (await c.GetStopsReport(vehicleId)).Result,
        (await c.GetTripsReport(vehicleId)).Result,
        (await c.GetMileagePeriodReport(vehicleId)).Result,
        (await c.GetVehicleCostEvolution(vehicleId)).Result
    };

    [Fact]
    public async Task IDOR_le_locataire_qui_demande_le_vehicule_d_un_autre_recoit_404_sur_toutes_les_routes()
    {
        using var ctx = await ContexteControleurSeedeAsync();

        // Véhicule 2 : même société, mais loué à quelqu'un d'autre.
        var reponses = await ToutesLesRoutesAsync(Rapports(ctx, Locataire()), 2);

        reponses.Should().AllSatisfy(r => r.Should().BeOfType<NotFoundObjectResult>(
            "on répond comme pour un véhicule inexistant : on ne révèle pas qu'il existe"));
    }

    [Fact]
    public async Task IDOR_le_locataire_garde_l_acces_a_son_propre_vehicule()
    {
        using var ctx = await ContexteControleurSeedeAsync();

        var reponses = await ToutesLesRoutesAsync(Rapports(ctx, Locataire()), 1);

        reponses.Should().AllSatisfy(r => r.Should().BeOfType<OkObjectResult>());
    }

    [Fact]
    public async Task IDOR_un_administrateur_sans_affectation_ouvre_n_importe_quel_vehicule_de_sa_societe()
    {
        using var ctx = await ContexteControleurSeedeAsync();

        var reponses = await ToutesLesRoutesAsync(Rapports(ctx, AdminSansAffectation()), 2);

        reponses.Should().AllSatisfy(r => r.Should().BeOfType<OkObjectResult>(
            "le cloisonnement société reste le seul filtre pour un administrateur"));
    }

    [Fact]
    public async Task IDOR_l_historique_GPS_d_un_vehicule_suit_la_meme_portee()
    {
        using var ctx = await ContexteControleurSeedeAsync();

        GpsController Gps(ICurrentTenantService tenant) =>
            new(ctx, null!, null!, null!, null!, tenant) { ControllerContext = ContexteHttp() };

        // Le rejeu du trajet d'un véhicule loué à un autre client : 404.
        (await Gps(Locataire()).GetVehicleHistory(2))
            .Should().BeOfType<NotFoundResult>();

        // Le sien reste accessible (aucune trame en base : réponse vide, pas un refus).
        (await Gps(Locataire()).GetVehicleHistory(1))
            .Should().BeOfType<OkObjectResult>();

        // L'administrateur sans affectation ouvre les deux.
        (await Gps(AdminSansAffectation()).GetVehicleHistory(2))
            .Should().BeOfType<OkObjectResult>();
    }
}
