using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using GpsController = global::GisAPI.Controllers.GpsController;
using GpsDeviceDto = global::GisAPI.Controllers.GpsDeviceDto;
using RealtimePositionDto = global::GisAPI.Controllers.RealtimePositionDto;
using VehiclePositionDto = global::GisAPI.Controllers.VehiclePositionDto;

namespace GisAPI.Tests.Application.Gps;

/// <summary>
/// Portée véhicules du contrôleur GPS — incident de confidentialité HERTZ (société 4).
///
/// HERTZ est un LOUEUR : ses 307 véhicules sont loués à des clients distincts, chacun
/// avec un compte restreint à ses propres véhicules (table user_vehicles). Le filtre
/// société ne cloisonne donc RIEN entre locataires.
///
/// Une première passe n'avait fermé qu'une seule route de ce contrôleur
/// (<c>vehicles/{id}/history</c>). Les autres ne vérifiaient que la société, et l'une
/// d'elles ANNULAIT ce correctif :
///
///   • <c>devices/{uid}/history</c> rendait EXACTEMENT la même trajectoire, indexée par
///     IMEI au lieu de l'identifiant du véhicule ;
///   • <c>devices</c> rendait à tout compte de la société les IMEI, libellés, numéros de
///     SIM ET le nom du véhicule affecté — la chaîne d'exploitation était complète et ne
///     demandait aucune devinette : lister les IMEI, puis lire l'historique ;
///   • <c>positions/realtime</c> et <c>positions/latest</c> rendaient les positions
///     temps réel, plaques et conducteurs de tout le parc — pour un loueur, « où sont en
///     ce moment les véhicules loués aux autres clients » ;
///   • <c>vehicles/{id}/position</c> et <c>vehicles/{id}/stats</c> étaient des IDOR : il
///     suffisait de changer l'identifiant dans l'URL.
///
/// Les tests appellent les MÉTHODES DE PRODUCTION du vrai contrôleur contre un
/// <see cref="GisDbContext"/> en mémoire : aucune copie de la règle, retirer un filtre
/// du contrôleur fait échouer ces tests.
///
/// PIÈGE COUVERT ICI — la portée a TROIS états et les confondre casse un des deux bouts :
///   • <c>null</c>    → administrateur, AUCUN filtre (tout le parc) ;
///   • liste non vide → ses véhicules ;
///   • liste VIDE     → non-administrateur sans affectation, il ne voit RIEN.
/// Le cas piège est l'administrateur SANS aucune ligne dans user_vehicles (utilisateur 11
/// de HERTZ) : il doit continuer à tout voir. D'où un TEST JUMEAU « admin » sur chacune
/// des routes fermées ici.
/// </summary>
public class GpsControllerVehicleScopeTests
{
    private const int CompanyId = 1;
    private const int AutreSociete = 2;

    /// <summary>Le locataire restreint (Kap Pharma, utilisateur 58) : un seul véhicule affecté.</summary>
    private const int LocataireUserId = 58;

    /// <summary>L'administrateur de la société SANS aucune affectation (utilisateur 11 de HERTZ).</summary>
    private const int AdminUserId = 11;

    // Véhicule A : loué au locataire. Véhicule B : loué à quelqu'un d'autre.
    private const int VehiculeA = 1;
    private const int VehiculeB = 2;
    private const int BoitierA = 11;
    private const int BoitierB = 12;
    /// <summary>Boîtier en stock, rattaché à AUCUN véhicule : hors de la portée de tout non-admin.</summary>
    private const int BoitierStock = 13;
    private const string ImeiA = "IMEI-A-0001";
    private const string ImeiB = "IMEI-B-0002";
    private const string ImeiStock = "IMEI-STOCK-0003";

    private static readonly DateTime Debut = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Fin = new(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);

    // ───────────────── Appelants ─────────────────

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Locataire() => Tenant(LocataireUserId, "Operateur");
    private static ICurrentTenantService AdminSansAffectation() => Tenant(AdminUserId, "company_admin");

    // ───────────────── Contexte et jeu de données ─────────────────

    /// <summary>Vrai GisDbContext en mémoire : ces routes filtrent DANS le contrôleur.</summary>
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

    /// <summary>
    /// Société 1 : véhicule A (loué au locataire) et véhicule B (loué à un autre client),
    /// chacun avec son boîtier et ses trames. Plus un boîtier en stock sans véhicule et un
    /// véhicule d'une autre société, qui ne doivent jamais apparaître.
    ///
    /// Seul le véhicule A est affecté au locataire. L'administrateur, lui, n'a AUCUNE
    /// ligne dans user_vehicles : c'est le cas piège à ne pas casser.
    /// </summary>
    private static async Task<ContexteControleur> ParcAsync()
    {
        var ctx = new ContexteControleur();

        ctx.Vehicles.AddRange(
            new Vehicle { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId, GpsDeviceId = BoitierA, Status = "active" },
            new Vehicle { Id = VehiculeB, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId, GpsDeviceId = BoitierB, Status = "active" },
            new Vehicle { Id = 9, Name = "Autre société", Plate = "999 TU 9", CompanyId = AutreSociete, GpsDeviceId = 19, Status = "active" });

        ctx.GpsDevices.AddRange(
            new GpsDevice { Id = BoitierA, DeviceUid = ImeiA, Label = "Boîtier A", SimNumber = "21600001", CompanyId = CompanyId, Status = "assigned" },
            new GpsDevice { Id = BoitierB, DeviceUid = ImeiB, Label = "Boîtier B", SimNumber = "21600002", CompanyId = CompanyId, Status = "assigned" },
            new GpsDevice { Id = BoitierStock, DeviceUid = ImeiStock, Label = "Stock", SimNumber = "21600003", CompanyId = CompanyId, Status = "unassigned" },
            new GpsDevice { Id = 19, DeviceUid = "IMEI-AUTRE", CompanyId = AutreSociete, Status = "assigned" });

        ctx.GpsPositions.AddRange(
            new GpsPosition { Id = 1, DeviceId = BoitierA, RecordedAt = Debut.AddDays(1), Latitude = 36.80, Longitude = 10.18, SpeedKph = 50, IsValid = true },
            new GpsPosition { Id = 2, DeviceId = BoitierA, RecordedAt = Debut.AddDays(2), Latitude = 36.81, Longitude = 10.19, SpeedKph = 60, IsValid = true },
            new GpsPosition { Id = 3, DeviceId = BoitierB, RecordedAt = Debut.AddDays(1), Latitude = 35.82, Longitude = 10.63, SpeedKph = 70, IsValid = true },
            new GpsPosition { Id = 4, DeviceId = BoitierB, RecordedAt = Debut.AddDays(2), Latitude = 35.83, Longitude = 10.64, SpeedKph = 80, IsValid = true });

        ctx.DeviceCommands.AddRange(
            new DeviceCommand { Id = 1, DeviceId = BoitierA, VehicleId = VehiculeA, UserId = AdminUserId, CommandType = "GO", CommandText = "AJ+GO#9999", Status = "sent", CompanyId = CompanyId },
            new DeviceCommand { Id = 2, DeviceId = BoitierB, VehicleId = VehiculeB, UserId = AdminUserId, CommandType = "GO", CommandText = "AJ+GO#9999", Status = "sent", CompanyId = CompanyId });

        ctx.UserVehicles.Add(new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = VehiculeA });

        await ctx.SaveChangesAsync();
        return ctx;
    }

    /// <summary>
    /// Positions temps réel telles que Redis les rend : par SOCIÉTÉ, donc tout le parc.
    /// C'est bien au contrôleur d'appliquer la portée avant de les enrichir.
    /// </summary>
    private static IRedisCacheService RedisAvecToutLeParc() => Redis(new List<VehiclePositionCache>
    {
        new() { DeviceUid = ImeiA, CompanyId = CompanyId, Latitude = 36.81, Longitude = 10.19, SpeedKph = 60, IsValid = true },
        new() { DeviceUid = ImeiB, CompanyId = CompanyId, Latitude = 35.83, Longitude = 10.64, SpeedKph = 80, IsValid = true }
    });

    /// <summary>Redis muet : le contrôleur bascule sur le repli base de données.</summary>
    private static IRedisCacheService RedisVide() => Redis(new List<VehiclePositionCache>());

    private static IRedisCacheService Redis(List<VehiclePositionCache> positions)
    {
        var m = new Mock<IRedisCacheService>();
        m.Setup(r => r.GetAllPositionsForCompanyAsync(It.IsAny<int>())).ReturnsAsync(positions);
        return m.Object;
    }

    private static GpsController Gps(ContexteControleur ctx, ICurrentTenantService tenant, IRedisCacheService? redis = null) =>
        new(ctx, null!, null!, redis ?? RedisVide(), null!, tenant)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, (tenant.UserId ?? 0).ToString())
                    }, "test"))
                }
            }
        };

    private static T Contenu<T>(ActionResult? resultat)
    {
        resultat.Should().BeOfType<OkObjectResult>();
        return (T)((OkObjectResult)resultat!).Value!;
    }

    // ───────────────── Listes de positions ─────────────────

    [Fact]
    public async Task Temps_reel_le_locataire_ne_recoit_que_son_vehicule()
    {
        using var ctx = await ParcAsync();

        var positions = Contenu<List<RealtimePositionDto>>(
            (await Gps(ctx, Locataire(), RedisAvecToutLeParc()).GetRealtimePositions()).Result);

        positions.Select(p => p.VehicleId).Should().Equal(new[] { VehiculeA },
            "pour un loueur, cette route dit où se trouvent EN CE MOMENT les véhicules loués aux autres clients");
        positions.Should().NotContain(p => p.DeviceUid == ImeiB);
    }

    [Fact]
    public async Task Temps_reel_le_repli_base_de_donnees_applique_la_meme_portee()
    {
        using var ctx = await ParcAsync();

        // Redis muet : le contrôleur repart de la base. Le second chemin avait la même fuite.
        var positions = Contenu<List<RealtimePositionDto>>(
            (await Gps(ctx, Locataire(), RedisVide()).GetRealtimePositions()).Result);

        positions.Select(p => p.VehicleId).Should().Equal(new[] { VehiculeA });
    }

    [Fact]
    public async Task Temps_reel_un_administrateur_sans_affectation_voit_tout_le_parc()
    {
        using var ctx = await ParcAsync();

        var positions = Contenu<List<RealtimePositionDto>>(
            (await Gps(ctx, AdminSansAffectation(), RedisAvecToutLeParc()).GetRealtimePositions()).Result);

        positions.Select(p => p.VehicleId).OrderBy(i => i).Should().Equal(new[] { VehiculeA, VehiculeB },
            "portée nulle = aucun filtre, surtout pas liste vide");
    }

    [Fact]
    public async Task Dernieres_positions_le_locataire_ne_recoit_que_son_vehicule()
    {
        using var ctx = await ParcAsync();

        var positions = Contenu<List<VehiclePositionDto>>(
            (await Gps(ctx, Locataire()).GetLatestPositions()).Result);

        positions.Select(p => p.VehicleId).Should().Equal(new[] { VehiculeA });
        positions.Should().NotContain(p => p.Plate == "222 TU 2", "la plaque d'un autre locataire n'a rien à faire ici");
    }

    [Fact]
    public async Task Dernieres_positions_un_administrateur_sans_affectation_voit_tout_le_parc()
    {
        using var ctx = await ParcAsync();

        var positions = Contenu<List<VehiclePositionDto>>(
            (await Gps(ctx, AdminSansAffectation()).GetLatestPositions()).Result);

        positions.Select(p => p.VehicleId).OrderBy(i => i).Should().Equal(new[] { VehiculeA, VehiculeB });
    }

    // ───────────────── Liste des boîtiers (l'amont de la chaîne d'exploitation) ─────────────────

    [Fact]
    public async Task Boitiers_le_locataire_ne_recoit_que_celui_de_son_vehicule()
    {
        using var ctx = await ParcAsync();

        var boitiers = Contenu<List<GpsDeviceDto>>(
            (await Gps(ctx, Locataire()).GetAllDevices()).Result);

        boitiers.Select(d => d.DeviceUid).Should().Equal(new[] { ImeiA },
            "c'est cette liste qui donnait les IMEI à essayer sur devices/{uid}/history");
        boitiers.Should().NotContain(d => d.DeviceUid == ImeiStock, "un boîtier sans véhicule n'entre dans la portée de personne");
    }

    [Fact]
    public async Task Boitiers_un_administrateur_sans_affectation_voit_tout_le_stock()
    {
        using var ctx = await ParcAsync();

        var boitiers = Contenu<List<GpsDeviceDto>>(
            (await Gps(ctx, AdminSansAffectation()).GetAllDevices()).Result);

        boitiers.Select(d => d.DeviceUid).OrderBy(u => u)
            .Should().Equal(new[] { ImeiA, ImeiB, ImeiStock });
    }

    [Fact]
    public async Task Boitiers_disponibles_rien_pour_un_non_administrateur_tout_pour_un_administrateur()
    {
        using var ctx = await ParcAsync();

        // Un boîtier « disponible » n'est rattaché à aucun véhicule : il portait pourtant
        // l'IMEI et le numéro de SIM de tout le stock de la société.
        Contenu<List<GpsDeviceDto>>((await Gps(ctx, Locataire()).GetAvailableDevices()).Result)
            .Should().BeEmpty();

        Contenu<List<GpsDeviceDto>>((await Gps(ctx, AdminSansAffectation()).GetAvailableDevices()).Result)
            .Select(d => d.DeviceUid).Should().Equal(new[] { ImeiStock });
    }

    // ───────────────── Historique par IMEI : la route qui annulait le correctif ─────────────────

    [Fact]
    public async Task Historique_par_IMEI_le_boitier_d_un_autre_locataire_rend_404()
    {
        using var ctx = await ParcAsync();

        (await Gps(ctx, Locataire()).GetDeviceHistory(ImeiB, Debut, Fin))
            .Should().BeOfType<NotFoundResult>(
                "cette route rend la MÊME trajectoire que vehicles/{id}/history, simplement indexée par IMEI");
    }

    [Fact]
    public async Task Historique_par_IMEI_le_locataire_garde_son_propre_boitier()
    {
        using var ctx = await ParcAsync();

        (await Gps(ctx, Locataire()).GetDeviceHistory(ImeiA, Debut, Fin))
            .Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Historique_par_IMEI_un_boitier_sans_vehicule_est_hors_de_portee_du_locataire()
    {
        using var ctx = await ParcAsync();

        (await Gps(ctx, Locataire()).GetDeviceHistory(ImeiStock, Debut, Fin))
            .Should().BeOfType<NotFoundResult>();

        (await Gps(ctx, AdminSansAffectation()).GetDeviceHistory(ImeiStock, Debut, Fin))
            .Should().BeOfType<OkObjectResult>("un administrateur garde l'accès au stock de sa société");
    }

    [Fact]
    public async Task Historique_par_IMEI_un_administrateur_sans_affectation_ouvre_n_importe_quel_boitier()
    {
        using var ctx = await ParcAsync();

        (await Gps(ctx, AdminSansAffectation()).GetDeviceHistory(ImeiB, Debut, Fin))
            .Should().BeOfType<OkObjectResult>();
    }

    // ───────────────── IDOR des routes « un véhicule » ─────────────────

    [Fact]
    public async Task IDOR_position_et_statistiques_du_vehicule_d_un_autre_rendent_404()
    {
        using var ctx = await ParcAsync();
        var c = Gps(ctx, Locataire());

        (await c.GetVehiclePosition(VehiculeB)).Result
            .Should().BeOfType<NotFoundResult>("on répond comme pour un véhicule inexistant : on ne révèle pas qu'il existe");

        (await c.GetVehicleGpsStats(VehiculeB, Debut, Fin))
            .Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task IDOR_le_locataire_garde_position_et_statistiques_de_son_vehicule()
    {
        using var ctx = await ParcAsync();
        var c = Gps(ctx, Locataire());

        (await c.GetVehiclePosition(VehiculeA)).Result.Should().BeOfType<OkObjectResult>();
        (await c.GetVehicleGpsStats(VehiculeA, Debut, Fin)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task IDOR_un_administrateur_sans_affectation_ouvre_n_importe_quel_vehicule()
    {
        using var ctx = await ParcAsync();
        var c = Gps(ctx, AdminSansAffectation());

        (await c.GetVehiclePosition(VehiculeB)).Result.Should().BeOfType<OkObjectResult>();
        (await c.GetVehicleGpsStats(VehiculeB, Debut, Fin)).Should().BeOfType<OkObjectResult>();
    }

    // ───────────────── Vue d'ensemble et boîtiers par identifiant ─────────────────

    [Fact]
    public async Task Vue_d_ensemble_compte_et_nomme_les_seuls_vehicules_de_la_portee()
    {
        using var ctx = await ParcAsync();

        var locataire = JsonSerializer.SerializeToElement(
            ((OkObjectResult)(await Gps(ctx, Locataire()).GetFleetOverview())!).Value);
        locataire.GetProperty("TotalVehicles").GetInt32().Should().Be(1);
        locataire.GetProperty("Vehicles").EnumerateArray()
            .Select(v => v.GetProperty("Id").GetInt32()).Should().Equal(new[] { VehiculeA });

        var admin = JsonSerializer.SerializeToElement(
            ((OkObjectResult)(await Gps(ctx, AdminSansAffectation()).GetFleetOverview())!).Value);
        admin.GetProperty("TotalVehicles").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Immobilisation_et_historique_de_commandes_suivent_la_portee()
    {
        using var ctx = await ParcAsync();
        var locataire = Gps(ctx, Locataire());

        // L'état porte le TEXTE des commandes du boîtier : hors portée, 404.
        (await locataire.GetImmobilizationState(BoitierB)).Should().BeOfType<NotFoundResult>();
        (await locataire.GetImmobilizationState(BoitierA)).Should().BeOfType<OkObjectResult>();

        // L'historique se vide au lieu d'échouer : l'écran affiche « aucune commande ».
        ((object[])((OkObjectResult)(await locataire.GetDeviceCommands(BoitierB))).Value!)
            .Should().BeEmpty();

        var admin = Gps(ctx, AdminSansAffectation());
        (await admin.GetImmobilizationState(BoitierB)).Should().BeOfType<OkObjectResult>();
    }
}
