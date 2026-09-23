using System.Data;
using System.Data.Common;
using FluentAssertions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;
using GeofencesController = global::GisAPI.Controllers.GeofencesController;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// TRADUCTION POSTGRESQL des requêtes de cloisonnement des géozones (23/09/2026).
///
/// Toutes les suites de confidentialité tournent sur le fournisseur InMemory, qui évalue
/// tout côté client : une projection ou un prédicat NON TRADUISIBLE en SQL y passe au
/// vert, puis lève « could not be translated » en production — au premier appel d'un
/// compte restreint, c'est-à-dire la page Géozones cassée pour les Opérateurs de SICOAC.
/// Le contrôleur disait lui-même qu'« AUCUN test n'exécute cette projection » et qu'il
/// fallait refaire le contrôle à la main avant chaque montée de version d'EF ou de Npgsql.
///
/// Ici, les MÉTHODES DE PRODUCTION du vrai contrôleur passent par le fournisseur Npgsql,
/// sans connexion (ouverture supprimée) et sans exécution (chaque commande est notée,
/// puis reçoit une réponse scriptée) : aucune base n'est touchée. Une requête qui ne se
/// traduit pas lève ; un filtre retiré disparaît du SQL noté.
///
/// L'identité est émise par <see cref="JetonDeProduction"/> : un vrai jeton, aucun claim
/// fabriqué à la main.
/// </summary>
public class GeozonesTraductionSqlTests
{
    private const int CompanyId = 1;
    private const int LocataireUserId = 58;
    private const int AdminUserId = 11;
    private const int VehiculeA = 1;
    private const int ZoneA = 101;

    /// <summary>Supprime l'ouverture de la connexion : rien ne part vers un serveur.</summary>
    private sealed class SansConnexion : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    /// <summary>
    /// Note le SQL de chaque commande et répond sans l'exécuter. Juste assez de données
    /// pour que le contrôleur aille jusqu'aux requêtes à vérifier :
    ///   • portée véhicules (user_vehicles) → le véhicule A ;
    ///   • case Géofences (users.can_geofences) → vraie ;
    ///   • identifiants visibles (règle EXISTS / NOT EXISTS) → la zone A ;
    ///   • tout le reste → aucune ligne.
    /// </summary>
    private sealed class SqlScripte : DbCommandInterceptor
    {
        public List<string> Commandes { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => InterceptionResult<DbDataReader>.SuppressWithResult(Repondre(command));

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(Repondre(command)));

        private DbDataReader Repondre(DbCommand command)
        {
            var sql = command.CommandText;
            Commandes.Add(sql);

            var table = new DataTable();
            if (sql.Contains("FROM user_vehicles"))
            {
                table.Columns.Add("vehicle_id", typeof(int));
                table.Rows.Add(VehiculeA);
            }
            else if (sql.Contains("can_geofences"))
            {
                table.Columns.Add("can_geofences", typeof(bool));
                table.Rows.Add(true);
            }
            else if (EstRequeteDIdentifiants(sql))
            {
                table.Columns.Add("id", typeof(int));
                table.Rows.Add(ZoneA);
            }
            return table.CreateDataReader();
        }
    }

    /// <summary>La requête de règle ne lit qu'une colonne d'identifiants, jamais le nom.</summary>
    private static bool EstRequeteDIdentifiants(string sql) =>
        sql.Contains("NOT EXISTS") && !sql.Contains(".name");

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        m.Setup(x => x.IsSystemAdmin).Returns(false);
        return m.Object;
    }

    private static (GisDbContext Contexte, SqlScripte Sql) ContexteNpgsql()
    {
        var sql = new SqlScripte();
        var ctx = new GisDbContext(
            new DbContextOptionsBuilder<GisDbContext>()
                .UseNpgsql("Host=localhost;Database=traduction_seulement")
                .AddInterceptors(new SansConnexion(), sql)
                .Options,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object);
        return (ctx, sql);
    }

    private static GeofencesController Zones(GisDbContext ctx, ICurrentTenantService t) =>
        new(ctx, Mock.Of<MediatR.IPublisher>(), t) { ControllerContext = JetonDeProduction.Contexte(t, CompanyId) };

    [Fact]
    public async Task La_regle_de_visibilite_d_un_restreint_se_traduit_en_EXISTS_et_NOT_EXISTS()
    {
        var (ctx, sql) = ContexteNpgsql();
        await using var _ = ctx;

        var resultat = (await Zones(ctx, Tenant(LocataireUserId, "Operateur")).GetGeofences()).Result;
        resultat.Should().BeOfType<OkObjectResult>("aucune partie de la requête ne doit être évaluée côté client");

        var regle = sql.Commandes.Should().ContainSingle(c => EstRequeteDIdentifiants(c)).Subject;
        regle.Should().Contain("FROM geofences")
            .And.Contain("geofence_vehicles")
            .And.Contain("EXISTS", "zones rattachées à l'un de SES véhicules")
            .And.Contain("NOT EXISTS", "zones rattachées à AUCUN véhicule (zones de société)")
            .And.Contain("= ANY", "la portée véhicules passe en UN paramètre tableau, pas en constantes");
    }

    [Fact]
    public async Task La_projection_de_la_liste_masque_les_vehicules_hors_portee_EN_SQL()
    {
        var (ctx, sql) = ContexteNpgsql();
        await using var _ = ctx;

        (await Zones(ctx, Tenant(LocataireUserId, "Operateur")).GetGeofences()).Result
            .Should().BeOfType<OkObjectResult>();

        var liste = sql.Commandes.Should().ContainSingle(c => c.Contains("FROM geofences") && c.Contains(".name")).Subject;
        liste.Should().Contain("geofence_vehicles").And.Contain("vehiculesVisibles",
            "le filtre des plaques est dans le SQL (un paramètre tableau) : la liste n'est jamais matérialisée entière puis filtrée en mémoire");
    }

    /// <summary>
    /// TEST JUMEAU — administrateur : le booléen capturé replie le filtre, la jointure des
    /// liaisons redevient nue. Deux formes SQL distinctes, donc deux entrées de cache de
    /// requête : un administrateur n'hérite jamais du plan filtré d'un locataire.
    /// </summary>
    [Fact]
    public async Task La_projection_de_la_liste_n_a_aucun_filtre_pour_l_administrateur()
    {
        var (ctx, sql) = ContexteNpgsql();
        await using var _ = ctx;

        (await Zones(ctx, Tenant(AdminUserId, "company_admin")).GetGeofences()).Result
            .Should().BeOfType<OkObjectResult>();

        sql.Commandes.Should().NotContain(c => c.Contains("FROM user_vehicles") || c.Contains("can_geofences"),
            "portée null : ni affectations ni case Géofences à lire");
        sql.Commandes.Should().ContainSingle(c => c.Contains("FROM geofences") && c.Contains(".name"))
            .Which.Should().NotContain("vehiculesVisibles");
    }

    /// <summary>
    /// GET /api/geofences/{id} passe désormais par la MÊME projection que la liste : sa
    /// traduction est vérifiée ici, pour le restreint comme pour l'administrateur.
    /// </summary>
    [Fact]
    public async Task Le_detail_d_une_zone_se_traduit_pour_le_restreint_et_pour_l_administrateur()
    {
        var (ctx, sql) = ContexteNpgsql();
        await using var _ = ctx;

        // Aucune ligne rendue par le script : la zone visible n'est pas trouvée, d'où 404.
        // Ce qui compte ici est que la requête ait été TRADUITE, donc notée.
        (await Zones(ctx, Tenant(LocataireUserId, "Operateur")).GetGeofence(ZoneA)).Result
            .Should().BeOfType<NotFoundResult>();
        sql.Commandes.Should().Contain(c => c.Contains("FROM geofences") && c.Contains(".name") && c.Contains("vehiculesVisibles"),
            "la projection du détail filtre les liaisons sur la portée, en SQL — comme la liste");

        var (ctxAdmin, sqlAdmin) = ContexteNpgsql();
        await using var __ = ctxAdmin;
        (await Zones(ctxAdmin, Tenant(AdminUserId, "company_admin")).GetGeofence(ZoneA)).Result
            .Should().BeOfType<NotFoundResult>();
        sqlAdmin.Commandes.Should().NotContain(c => c.Contains("FROM user_vehicles"),
            "administrateur : portée null, aucune lecture des affectations");
        sqlAdmin.Commandes.Should().ContainSingle(c => c.Contains("FROM geofences") && c.Contains(".name"))
            .Which.Should().NotContain("vehiculesVisibles");
    }

    [Fact]
    public async Task Les_groupes_visibles_se_traduisent_en_SQL()
    {
        var (ctx, sql) = ContexteNpgsql();
        await using var _ = ctx;

        (await Zones(ctx, Tenant(LocataireUserId, "Operateur")).GetGroups()).Result
            .Should().BeOfType<OkObjectResult>();

        sql.Commandes.Should().Contain(c => c.Contains("FROM geofence_groups") && c.Contains("NOT EXISTS"),
            "groupe visible = au moins une zone visible, OU groupe vide pour un gestionnaire de zones");
    }
}
