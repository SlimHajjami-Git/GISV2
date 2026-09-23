using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Services;

/// <summary>
/// Incident de confidentialité HERTZ du 23/09/2026 — HERTZ est un LOUEUR : ses
/// véhicules sont loués à des clients différents, et chaque locataire a un compte
/// restreint à SES véhicules (table user_vehicles). Un locataire affecté à
/// 2 véhicules sur 307 voyait pourtant, sur son tableau de bord, les données des
/// 305 autres.
///
/// Trois blocs du tableau de bord avaient oublié la portée véhicules
/// (<c>scopeIds</c>), pourtant calculée ligne 70 de <c>DashboardService</c> et
/// appliquée sept fois ailleurs dans la même méthode :
///   • bloc « Alertes » : les 20 dernières <c>gps_alerts</c> de TOUT le parc, et
///     les 20 dernières <c>notifications</c> de TOUTE la société (une notification
///     appartient pourtant à son DESTINATAIRE — la cloche, elle, filtre bien sur
///     <c>UserId</c> depuis <c>GetNotificationsQueryHandler</c>) ;
///   • bloc « Santé » : les compteurs Sain/Attention/Critique portaient sur le
///     parc entier (<c>CalculateAllScoresAsync</c> note toute la société) ;
///   • bloc « Géofences » : les compteurs de passages additionnaient les
///     franchissements de tous les véhicules.
///
/// Les tests appellent les MÉTHODES DE PRODUCTION (<c>AlertFeedAsync</c>,
/// <c>ScopedHealthResults</c>, <c>GeofencePassageCountsAsync</c>) contre le
/// contexte SQLite en mémoire : aucune copie de la règle, retirer un filtre du
/// service fait échouer ces tests.
///
/// PIÈGE COUVERT ICI — la portée a TROIS états et les confondre casse un des deux
/// bouts : <c>null</c> = administrateur, aucun filtre ; liste non vide = ses
/// véhicules ; liste VIDE = non-admin sans affectation, il ne voit RIEN. D'où le
/// test jumeau <see cref="Un_admin_sans_affectation_voit_tout_le_parc"/> : chez
/// HERTZ l'administrateur n'a AUCUNE ligne dans user_vehicles et doit continuer à
/// tout voir.
/// </summary>
public class DashboardAlertScopeTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;

    /// <summary>Locataire type : non-admin, affecté au seul véhicule 1.</summary>
    private const int LocataireUserId = 61;
    /// <summary>Administrateur de la société, ZÉRO ligne dans user_vehicles (cas HERTZ).</summary>
    private const int AdminUserId = 62;
    /// <summary>Autre utilisateur de la société : destinataire de notifications qui ne regardent pas le locataire.</summary>
    private const int AutreUserId = 63;
    /// <summary>Non-admin sans aucune affectation : portée VIDE, il ne voit rien.</summary>
    private const int SansAffectationUserId = 64;

    private static readonly DateTime PeriodStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);

    private static DateTime Sept(int day) => new(2026, 9, day, 8, 0, 0, DateTimeKind.Utc);

    // ── Les appels de production, sans copie ────────────────────────────────────
    private static Task<List<int>?> ScopeIdsAsync(TestGisDbContext ctx, bool isAdmin, int userId) =>
        DashboardService.ScopeIdsAsync(ctx, isAdmin, userId, CancellationToken.None);

    private static Task<List<DashboardService.DashboardAlert>> AlertFeedAsync(
        TestGisDbContext ctx, int userId, List<int>? scopeIds) =>
        DashboardService.AlertFeedAsync(ctx, CompanyId, userId, scopeIds, CancellationToken.None);

    private static Task<Dictionary<int, int>> GeofencePassagesAsync(TestGisDbContext ctx, List<int>? scopeIds) =>
        DashboardService.GeofencePassageCountsAsync(ctx, CompanyId, scopeIds, PeriodStart, PeriodEnd, CancellationToken.None);

    /// <summary>
    /// Les trois compteurs du bloc « Santé », avec les MÊMES seuils que le service
    /// (≥ 80 sain, 40-79 à surveiller, &lt; 40 critique). Leur somme est le nombre de
    /// véhicules pris en compte : c'est ce total que le locataire voyait à 3 au lieu de 1.
    /// </summary>
    private static async Task<(int Healthy, int Attention, int Unhealthy)> HealthCountsAsync(
        TestGisDbContext ctx, List<int>? scopeIds)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IGisDbContext>(ctx)
            .BuildServiceProvider();
        var service = new VehicleHealthScoreService(provider);

        var scoped = DashboardService.ScopedHealthResults(await service.CalculateAllScoresAsync(CompanyId), scopeIds);
        return (scoped.Count(h => h.Score >= 80),
                scoped.Count(h => h.Score >= 40 && h.Score < 80),
                scoped.Count(h => h.Score < 40));
    }

    /// <summary>
    /// Société 1 : trois véhicules, dont le seul véhicule 1 est affecté au locataire.
    /// Une alerte GPS par véhicule, une notification par destinataire, et des passages
    /// de géofence répartis sur les trois véhicules.
    /// </summary>
    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Loué Kap", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 3, Name = "Loué Djerba", Plate = "333 TU 3", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId });

        ctx.GpsAlerts.AddRange(
            new GpsAlert { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "overspeed", Severity = "critical",
                           Message = "Alerte vehicule 1", Timestamp = Sept(10) },
            new GpsAlert { Id = 2, CompanyId = CompanyId, VehicleId = 2, Type = "overspeed", Severity = "warning",
                           Message = "Alerte vehicule 2", Timestamp = Sept(11) },
            new GpsAlert { Id = 3, CompanyId = CompanyId, VehicleId = 3, Type = "overspeed", Severity = "info",
                           Message = "Alerte vehicule 3", Timestamp = Sept(12) },
            // Autre société : jamais visible, le cloisonnement EF s'en charge déjà.
            new GpsAlert { Id = 4, CompanyId = OtherCompanyId, VehicleId = 9, Type = "overspeed", Severity = "critical",
                           Message = "Alerte societe 2", Timestamp = Sept(13) });

        ctx.Notifications.AddRange(
            new Notification { Id = 1, CompanyId = CompanyId, UserId = LocataireUserId, Type = "maintenance",
                               Title = "Notification du locataire", Priority = "high", CreatedAt = Sept(14) },
            new Notification { Id = 2, CompanyId = CompanyId, UserId = AutreUserId, Type = "maintenance",
                               Title = "Notification d un autre utilisateur", Priority = "high", CreatedAt = Sept(15) },
            new Notification { Id = 3, CompanyId = CompanyId, UserId = AdminUserId, Type = "maintenance",
                               Title = "Notification de l admin", Priority = "medium", CreatedAt = Sept(16) });

        ctx.GeofenceEvents.AddRange(
            new GeofenceEvent { Id = 1, CompanyId = CompanyId, GeofenceId = 1, VehicleId = 1, Type = "entry", Timestamp = Sept(2) },
            new GeofenceEvent { Id = 2, CompanyId = CompanyId, GeofenceId = 1, VehicleId = 1, Type = "exit", Timestamp = Sept(3) },
            new GeofenceEvent { Id = 3, CompanyId = CompanyId, GeofenceId = 1, VehicleId = 2, Type = "entry", Timestamp = Sept(4) },
            new GeofenceEvent { Id = 4, CompanyId = CompanyId, GeofenceId = 1, VehicleId = 2, Type = "exit", Timestamp = Sept(5) },
            new GeofenceEvent { Id = 5, CompanyId = CompanyId, GeofenceId = 1, VehicleId = 3, Type = "entry", Timestamp = Sept(6) },
            new GeofenceEvent { Id = 6, CompanyId = CompanyId, GeofenceId = 2, VehicleId = 3, Type = "entry", Timestamp = Sept(7) });

        // Le locataire n'a QUE le véhicule 1. L'admin (62) n'a AUCUNE ligne ici.
        ctx.UserVehicles.Add(new UserVehicle { UserId = LocataireUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Un_locataire_ne_voit_que_les_alertes_de_ses_vehicules()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        scope.Should().Equal(new[] { 1 });

        var feed = await AlertFeedAsync(ctx, LocataireUserId, scope);

        feed.Select(a => a.Message).Should().BeEquivalentTo(new[]
        {
            "Alerte vehicule 1",
            "Notification du locataire"
        }, "une alerte suit le VÉHICULE et une notification suit son DESTINATAIRE");

        feed.Should().NotContain(a => a.Message == "Alerte vehicule 2",
            "c'est le symptôme signalé : il recevait les alertes des véhicules loués à d'autres clients");
        feed.Should().NotContain(a => a.Message == "Alerte vehicule 3");
        feed.Should().NotContain(a => a.Message == "Notification d un autre utilisateur",
            "une notification appartient à son destinataire, comme dans la cloche");
    }

    [Fact]
    public async Task Un_locataire_ne_compte_que_la_sante_de_ses_vehicules()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        var (healthy, attention, unhealthy) = await HealthCountsAsync(ctx, scope);

        (healthy + attention + unhealthy).Should().Be(1,
            "les compteurs Sain/Attention/Critique portent sur SES véhicules, pas sur les 3 du parc");
    }

    [Fact]
    public async Task Un_locataire_ne_compte_que_ses_passages_de_geofence()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        var passages = await GeofencePassagesAsync(ctx, scope);

        passages.Should().Equal(new Dictionary<int, int> { [1] = 2 },
            "seuls les 2 franchissements du véhicule 1 comptent ; la zone 2, qu'il n'a jamais franchie, n'a aucun passage");
    }

    /// <summary>
    /// TEST JUMEAU — le correctif ne doit RIEN changer pour un administrateur.
    /// Chez HERTZ, l'administrateur de la société n'a AUCUNE ligne dans
    /// user_vehicles : confondre « admin » (portée <c>null</c>) avec « non-admin
    /// sans affectation » (portée VIDE) lui viderait tout son tableau de bord.
    /// Décision produit du 23/09/2026 : un admin continue de voir TOUTES les
    /// notifications de sa société, pas seulement les siennes.
    /// </summary>
    [Fact]
    public async Task Un_admin_sans_affectation_voit_tout_le_parc()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ctx.UserVehicles.CountAsync(uv => uv.UserId == AdminUserId))
            .Should().Be(0, "l'administrateur HERTZ n'a aucune affectation : c'est bien le cas piège");

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: AdminUserId);
        scope.Should().BeNull("un admin n'est jamais restreint par les affectations");

        var feed = await AlertFeedAsync(ctx, AdminUserId, scope);
        feed.Select(a => a.Message).Should().BeEquivalentTo(new[]
        {
            "Alerte vehicule 1", "Alerte vehicule 2", "Alerte vehicule 3",
            "Notification du locataire", "Notification d un autre utilisateur", "Notification de l admin"
        }, "les 3 alertes du parc et les 3 notifications de la société, sans l'alerte de la société 2");

        var (healthy, attention, unhealthy) = await HealthCountsAsync(ctx, scope);
        (healthy + attention + unhealthy).Should().Be(3, "l'admin voit les 3 véhicules");

        (await GeofencePassagesAsync(ctx, scope))
            .Should().Equal(new Dictionary<int, int> { [1] = 5, [2] = 1 });
    }

    /// <summary>
    /// Le pré-chauffage appelle <c>ComputeDashboardAllAsync</c> avec
    /// <c>userId: 0, isAdmin: true</c> et met le résultat dans l'entrée ADMIN du
    /// cache, servie à tous les admins de la société. Un filtre inconditionnel sur
    /// <c>UserId == userId</c> aurait donc vidé les notifications de cette entrée.
    /// </summary>
    [Fact]
    public async Task Le_prechauffage_admin_ne_filtre_pas_sur_l_utilisateur_zero()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: 0);
        scope.Should().BeNull();

        var feed = await AlertFeedAsync(ctx, userId: 0, scopeIds: scope);
        feed.Should().HaveCount(6, "aucune notification n'est adressée à l'utilisateur 0 : les filtrer viderait l'entrée pré-chauffée");
    }

    [Fact]
    public async Task Un_non_admin_sans_affectation_ne_voit_rien()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: SansAffectationUserId);
        scope.Should().NotBeNull().And.BeEmpty("liste vide = aucun véhicule visible, surtout pas tout le parc");

        (await AlertFeedAsync(ctx, SansAffectationUserId, scope)).Should().BeEmpty();

        var (healthy, attention, unhealthy) = await HealthCountsAsync(ctx, scope);
        (healthy + attention + unhealthy).Should().Be(0);

        (await GeofencePassagesAsync(ctx, scope)).Should().BeEmpty();
    }
}
