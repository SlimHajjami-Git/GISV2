using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Features.AlertEmails;
using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Application.Features.AlertEmails.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using AlertEmailsController = global::GisAPI.Controllers.AlertEmailsController;
using CompanyController = global::GisAPI.Controllers.CompanyController;
using DeviceCheckController = global::GisAPI.Controllers.DeviceCheckController;
using DeviceEventsController = global::GisAPI.Controllers.DeviceEventsController;
using TramOnLineController = global::GisAPI.Controllers.TramOnLineController;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// ROUTES OUVERTES de l'incident HERTZ (société 4) — quatrième passe.
///
/// Les deux premières passes avaient fermé le tableau de bord, les rapports, « /api/gps »,
/// le hub SignalR et le rapport e-mail ; la troisième, les routes sœurs. Une relecture
/// adversariale a trouvé ce qui restait GRAND OUVERT :
///
/// <list type="bullet">
/// <item><b>« /api/devicecheck/lookup » sans AUCUNE authentification</b> — la classe ne
/// portait pas d'attribut <c>[Authorize]</c> et l'application ne déclare aucune
/// <c>FallbackPolicy</c> ; <c>PermissionMiddleware</c> rend la main dès qu'une requête n'est
/// pas authentifiée. La requête levait en plus les filtres multi-tenant sur GpsDevices ET
/// sur Vehicles : avec une plaque, qui se lit dans la rue, n'importe qui obtenait la
/// dernière position, le contact, les coordonnées, le compteur et le carburant de n'importe
/// quel véhicule de n'importe quel client.</item>
/// <item><b>« POST /api/alertemails »</b> — abonnement e-mail aux échéances de TOUT le parc,
/// sans permission ni portée.</item>
/// <item><b>« /api/companies/{id}/users » et « /api/companies/{id}/stats »</b> — les deux
/// dernières lectures du contrôleur restées sans garde-fou.</item>
/// <item><b>« POST /api/tramonline/register/{mat} »</b> — création d'un véhicule sans
/// permission, et dans la PREMIÈRE société de la plate-forme au lieu de celle de
/// l'appelant.</item>
/// <item><b>« POST /api/device-events/{id}/acknowledge »</b> — le contrôle de portée ajouté
/// à la passe précédente ne servait à rien : la route lisait un claim « userId » que le
/// jeton ne porte pas, donc elle répondait 401 à tout le monde.</item>
/// </list>
///
/// <para>Les tests appellent les MÉTHODES DE PRODUCTION des vrais contrôleurs contre un
/// <see cref="GisDbContext"/> en mémoire, dont le service de tenant est CELUI DE L'APPELANT
/// — comme en production, où la même instance scopée sert au contexte et au contrôleur.</para>
///
/// <para><b>Les jetons de test ne portent QUE les claims que JwtService écrit vraiment</b> :
/// « sub » projeté sur <see cref="ClaimTypes.NameIdentifier"/>, « companyId », les rôles. Pas
/// de claim « userId » de complaisance — c'est précisément ce genre de faux claim dans un
/// utilitaire de test qui a laissé croire que l'acquittement fonctionnait.</para>
///
/// <para>PIÈGE COUVERT PARTOUT — la portée a TROIS états : <c>null</c> = administrateur,
/// AUCUN filtre ; liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN. Le cas à ne
/// pas casser est l'administrateur SANS aucune ligne dans user_vehicles (utilisateur 11 de
/// HERTZ) : d'où un TEST JUMEAU « administrateur » sur chaque route fermée.</para>
/// </summary>
public class RoutesOuvertesTests
{
    private const int Societe = 1;
    private const int AutreSociete = 2;

    /// <summary>Le locataire restreint (Kap Pharma, utilisateur 58) : un seul véhicule affecté.</summary>
    private const int LocataireUserId = 58;

    /// <summary>L'administrateur de la société SANS aucune affectation (utilisateur 11 de HERTZ).</summary>
    private const int AdminUserId = 11;

    private const int AdminSystemeUserId = 3;

    private const int VehiculeA = 1;   // loué au locataire
    private const int VehiculeB = 2;   // loué à un autre client
    private const int VehiculeAutreSociete = 9;

    private const int BoitierA = 11;
    private const int BoitierB = 12;
    private const int BoitierStock = 13;               // en stock, rattaché à AUCUN véhicule
    private const int BoitierAutreSociete = 19;

    private const string PlaqueA = "111 TU 1";
    private const string PlaqueB = "222 TU 2";
    private const string PlaqueAutreSociete = "999 TU 9";

    private static readonly DateTime Jour = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);

    // ───────────────── Appelants ─────────────────

    private static ICurrentTenantService Tenant(int userId, int societe, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(societe);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        m.Setup(x => x.IsSystemAdmin).Returns(roles.Contains("system_admin"));
        return m.Object;
    }

    private static ICurrentTenantService Locataire() => Tenant(LocataireUserId, Societe, "Operateur");
    private static ICurrentTenantService AdminSansAffectation() => Tenant(AdminUserId, Societe, "company_admin");
    private static ICurrentTenantService AdminSysteme() => Tenant(AdminSystemeUserId, Societe, "system_admin");
    private static ICurrentTenantService AdminAutreSociete() => Tenant(21, AutreSociete, "company_admin");

    // ───────────────── Contexte et jeu de données ─────────────────

    /// <summary>Vrai GisDbContext en mémoire : ces routes filtrent DANS le contrôleur.</summary>
    private sealed class ContexteControleur : GisDbContext
    {
        public ContexteControleur(string magasin, ICurrentTenantService tenant)
            : base(new DbContextOptionsBuilder<GisDbContext>()
                    .UseInMemoryDatabase(magasin, b => b.EnableNullChecks(false))
                    .Options,
                tenant) { }

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
    /// Un magasin en mémoire partagé, et un contexte PAR APPELANT au-dessus : les filtres
    /// globaux de GisDbContext dépendent du service de tenant, il faut donc que celui du
    /// contexte soit celui du contrôleur, exactement comme la DI le fait en production.
    /// </summary>
    private sealed class Parc : IDisposable
    {
        private readonly string _magasin = Guid.NewGuid().ToString();
        private readonly List<ContexteControleur> _ouverts = new();

        public ContexteControleur Pour(ICurrentTenantService tenant)
        {
            var ctx = new ContexteControleur(_magasin, tenant);
            _ouverts.Add(ctx);
            return ctx;
        }

        public void Dispose()
        {
            foreach (var ctx in _ouverts) ctx.Dispose();
        }
    }

    /// <summary>
    /// Société 1 : véhicule A (loué au locataire) et véhicule B (loué à un autre client),
    /// chacun avec son boîtier et sa dernière trame. Plus un boîtier en stock et un véhicule
    /// d'une AUTRE société, qui ne doivent jamais apparaître.
    ///
    /// Seul le véhicule A est affecté au locataire ; l'administrateur n'a AUCUNE ligne dans
    /// user_vehicles — c'est le cas piège à ne pas casser.
    /// </summary>
    private static async Task<Parc> ParcAsync()
    {
        var parc = new Parc();

        // Semis avec les filtres levés (administrateur système), sinon la société 2 ne
        // pourrait pas être écrite depuis un contexte borné à la société 1.
        var semis = parc.Pour(AdminSysteme());

        semis.Societes.AddRange(
            new Societe { Id = Societe, Name = "HERTZ", Email = "contact@hertz.tn", IsActive = true },
            new Societe { Id = AutreSociete, Name = "SICOAC", Email = "contact@sicoac.tn", IsActive = true });

        semis.Roles.AddRange(
            new Role { Id = 1, Name = "Operateur" },
            new Role { Id = 2, Name = "company_admin", IsCompanyAdmin = true });

        semis.Users.AddRange(
            new User { Id = 77, CompanyId = Societe, RoleId = 1, Email = "autre@locataire.tn", FirstName = "Autre", LastName = "Locataire", EmployeeRole = "operateur" },
            new User { Id = LocataireUserId, CompanyId = Societe, RoleId = 1, Email = "kap@pharma.tn", FirstName = "Kap", LastName = "Pharma", EmployeeRole = "operateur" },
            new User { Id = AdminUserId, CompanyId = Societe, RoleId = 2, Email = "admin@hertz.tn", FirstName = "Admin", LastName = "Hertz", EmployeeRole = "gestionnaire" },
            new User { Id = 21, CompanyId = AutreSociete, RoleId = 2, Email = "admin@sicoac.tn", FirstName = "Admin", LastName = "Sicoac", EmployeeRole = "gestionnaire" });

        semis.Vehicles.AddRange(
            new Vehicle { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = PlaqueA, CompanyId = Societe, GpsDeviceId = BoitierA, Status = "active", Mileage = 120_000, FuelTankCapacity = 60 },
            new Vehicle { Id = VehiculeB, Name = "Loué Carthage", Plate = PlaqueB, CompanyId = Societe, GpsDeviceId = BoitierB, Status = "active", Mileage = 80_000, FuelTankCapacity = 60 },
            new Vehicle { Id = VehiculeAutreSociete, Name = "Autre société", Plate = PlaqueAutreSociete, CompanyId = AutreSociete, GpsDeviceId = BoitierAutreSociete, Status = "active", Mileage = 10_000, FuelTankCapacity = 60 });

        semis.GpsDevices.AddRange(
            new GpsDevice { Id = BoitierA, DeviceUid = "IMEI-A", Mat = "MAT-A", Label = "Boîtier A", SimNumber = "21600001", CompanyId = Societe, Status = "active" },
            new GpsDevice { Id = BoitierB, DeviceUid = "IMEI-B", Mat = "MAT-B", Label = "Boîtier B", SimNumber = "21600002", CompanyId = Societe, Status = "active" },
            new GpsDevice { Id = BoitierStock, DeviceUid = "IMEI-STOCK", Mat = "MAT-STOCK", Label = "Stock", SimNumber = "21600003", CompanyId = Societe, Status = "unassigned" },
            new GpsDevice { Id = BoitierAutreSociete, DeviceUid = "IMEI-SICOAC", Mat = "MAT-SICOAC", Label = "Boîtier SICOAC", SimNumber = "21600009", CompanyId = AutreSociete, Status = "active" });

        semis.GpsPositions.AddRange(
            new GpsPosition { Id = 1, DeviceId = BoitierA, RecordedAt = Jour, Latitude = 36.80, Longitude = 10.18, SpeedKph = 50, IgnitionOn = true, IsValid = true },
            new GpsPosition { Id = 2, DeviceId = BoitierB, RecordedAt = Jour, Latitude = 35.82, Longitude = 10.63, SpeedKph = 70, IgnitionOn = true, IsValid = true },
            new GpsPosition { Id = 3, DeviceId = BoitierStock, RecordedAt = Jour, Latitude = 36.00, Longitude = 10.00, SpeedKph = 0, IgnitionOn = false, IsValid = true },
            new GpsPosition { Id = 4, DeviceId = BoitierAutreSociete, RecordedAt = Jour, Latitude = 34.74, Longitude = 10.76, SpeedKph = 90, IgnitionOn = true, IsValid = true });

        semis.DeviceEvents.AddRange(
            new DeviceEvent { Id = 1, DeviceId = BoitierA, VehicleId = VehiculeA, CompanyId = Societe, EventType = "disconnect", EventAt = Jour, LastKnownLat = 36.8, LastKnownLon = 10.1 },
            new DeviceEvent { Id = 2, DeviceId = BoitierB, VehicleId = VehiculeB, CompanyId = Societe, EventType = "disconnect", EventAt = Jour, LastKnownLat = 35.8, LastKnownLon = 10.6 },
            new DeviceEvent { Id = 3, DeviceId = BoitierStock, VehicleId = null, CompanyId = Societe, EventType = "restart", EventAt = Jour });

        semis.UserVehicles.AddRange(
            new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = VehiculeA },
            new UserVehicle { Id = 2, UserId = 77, VehicleId = VehiculeB });

        await semis.SaveChangesAsync();
        return parc;
    }

    /// <summary>
    /// Jeton de test fidèle à <c>JwtService</c> : « sub » (projeté sur NameIdentifier),
    /// « companyId », les rôles. AUCUN claim « userId » — il n'existe pas en production.
    /// </summary>
    private static ControllerContext Contexte(ICurrentTenantService tenant)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, (tenant.UserId ?? 0).ToString()),
            new("companyId", (tenant.CompanyId ?? 0).ToString())
        };
        claims.AddRange(tenant.UserRoles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    private static DeviceCheckController Boitier(ContexteControleur ctx, ICurrentTenantService t) =>
        new(ctx, t) { ControllerContext = Contexte(t) };

    private static TramOnLineController Tram(ContexteControleur ctx, ICurrentTenantService t) =>
        new(ctx, Microsoft.Extensions.Logging.Abstractions.NullLogger<TramOnLineController>.Instance, t)
        { ControllerContext = Contexte(t) };

    private static CompanyController Societes(ContexteControleur ctx, ICurrentTenantService t) =>
        new(ctx, t) { ControllerContext = Contexte(t) };

    private static DeviceEventsController Evenements(ContexteControleur ctx, ICurrentTenantService t) =>
        new(ctx, t) { ControllerContext = Contexte(t) };

    /// <summary>Valeur d'une propriété d'un objet anonyme rendu par un contrôleur.</summary>
    private static T Champ<T>(object corps, string nom) =>
        (T)corps.GetType().GetProperty(nom)!.GetValue(corps)!;

    private static object CorpsOk(ActionResult? resultat)
    {
        resultat.Should().BeOfType<OkObjectResult>();
        return ((OkObjectResult)resultat!).Value!;
    }

    // ═════════════════ B1 — « /api/devicecheck » ═════════════════

    /// <summary>
    /// Inventaire exhaustif des actions de contrôleur qui ne sont couvertes NI par un
    /// <c>[Authorize]</c> (classe ou méthode) NI par un <c>[AllowAnonymous]</c> explicite.
    ///
    /// <para>Un appel non authentifié se refuse par un attribut, pas dans le corps de la
    /// méthode : un test qui appellerait <c>Lookup</c> directement ne prouverait rien, parce
    /// que les filtres d'autorisation ne tournent pas sur un appel de méthode. C'est donc
    /// l'ATTRIBUT qui est vérifié, sur TOUT le contrôleur d'API, par réflexion.</para>
    ///
    /// <para>La liste attendue est l'inventaire RELU : trois actions d'authentification qui
    /// ne peuvent pas exiger de jeton (se connecter, rafraîchir) ou qui se gardent
    /// autrement (le semis, refusé hors environnement Development). Toute route ouverte qui
    /// apparaîtra demain fera échouer ce test — c'est le but.</para>
    /// </summary>
    [Fact]
    public void Aucune_route_n_est_ouverte_sans_decision_ecrite()
    {
        var attendu = new[]
        {
            "AuthController.Login",          // se connecter : aucun jeton par définition
            "AuthController.Refresh",        // rafraîchir un jeton expiré
            "AuthController.SeedDatabase",   // semis, refusé hors ASPNETCORE_ENVIRONMENT=Development
        };

        var ouvertes = ActionsNonCouvertes().OrderBy(x => x, StringComparer.Ordinal).ToArray();

        ouvertes.Should().BeEquivalentTo(attendu,
            "« /api/devicecheck/lookup » répondait 200 avec la dernière position de n'importe "
            + "quel véhicule SANS jeton : la classe ne portait aucun [Authorize] et Program.cs "
            + "ne déclare aucune FallbackPolicy");
    }

    [Fact]
    public void Le_controleur_de_verification_de_boitier_exige_desormais_un_jeton()
    {
        var type = typeof(DeviceCheckController);

        type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().NotBeEmpty(
            "sans cet attribut la route est anonyme : Program.cs n'enchaîne que UseAuthentication/UseAuthorization");
        type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        type.GetMethod(nameof(DeviceCheckController.Lookup))!
            .GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
    }

    private static IEnumerable<string> ActionsNonCouvertes()
    {
        var assemblage = typeof(DeviceCheckController).Assembly;

        foreach (var type in assemblage.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            // Décision écrite AU NIVEAU DE LA CLASSE : [Authorize] ferme tout le contrôleur,
            // [AllowAnonymous] l'ouvre explicitement (AssistantController, copilote d'avant
            // connexion, qui ne charge aucune donnée de société).
            var classeAutorisee = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                || type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            foreach (var methode in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (methode.IsSpecialName) continue;
                if (methode.GetCustomAttributes<NonActionAttribute>(inherit: true).Any()) continue;
                if (!methode.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any()) continue;

                var couverte = classeAutorisee
                    || methode.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                    || methode.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

                if (!couverte)
                    yield return $"{type.Name}.{methode.Name}";
            }
        }
    }

    [Fact]
    public async Task Verification_de_boitier_une_plaque_d_une_AUTRE_societe_reste_introuvable()
    {
        using var parc = await ParcAsync();
        var admin = Boitier(parc.Pour(AdminSansAffectation()), AdminSansAffectation());

        var corps = CorpsOk(await admin.Lookup(PlaqueAutreSociete));

        Champ<bool>(corps, "found").Should().BeFalse(
            "la requête levait les filtres multi-tenant (IgnoreQueryFilters) sur GpsDevices ET Vehicles : "
            + "une plaque relevée dans la rue rendait la position d'un véhicule d'un autre client");

        // …et l'administrateur de l'AUTRE société, lui, la voit.
        var sicoac = Boitier(parc.Pour(AdminAutreSociete()), AdminAutreSociete());
        Champ<bool>(CorpsOk(await sicoac.Lookup(PlaqueAutreSociete)), "found").Should().BeTrue();
    }

    [Fact]
    public async Task Verification_de_boitier_par_IMEI_ou_MAT_d_une_AUTRE_societe_reste_introuvable()
    {
        using var parc = await ParcAsync();
        var admin = Boitier(parc.Pour(AdminSansAffectation()), AdminSansAffectation());

        Champ<bool>(CorpsOk(await admin.Lookup("IMEI-SICOAC")), "found").Should().BeFalse();
        Champ<bool>(CorpsOk(await admin.Lookup("MAT-SICOAC")), "found").Should().BeFalse();
    }

    [Fact]
    public async Task Verification_de_boitier_le_locataire_ne_voit_que_son_vehicule()
    {
        using var parc = await ParcAsync();
        var locataire = Boitier(parc.Pour(Locataire()), Locataire());

        Champ<bool>(CorpsOk(await locataire.Lookup(PlaqueB)), "found").Should().BeFalse(
            "chez un LOUEUR le filtre société ne cloisonne RIEN entre locataires : il faut la portée véhicule");
        Champ<bool>(CorpsOk(await locataire.Lookup("IMEI-B")), "found").Should().BeFalse();
        Champ<bool>(CorpsOk(await locataire.Lookup("MAT-STOCK")), "found").Should().BeFalse(
            "un boîtier en stock n'est rattaché à aucun véhicule : il n'entre dans la portée de personne");

        Champ<bool>(CorpsOk(await locataire.Lookup(PlaqueA)), "found").Should().BeTrue(
            "le véhicule qui lui est loué reste consultable");
    }

    [Fact]
    public async Task Verification_de_boitier_l_administrateur_sans_affectation_voit_tout_le_parc()
    {
        using var parc = await ParcAsync();
        var admin = Boitier(parc.Pour(AdminSansAffectation()), AdminSansAffectation());

        Champ<bool>(CorpsOk(await admin.Lookup(PlaqueA)), "found").Should().BeTrue();
        Champ<bool>(CorpsOk(await admin.Lookup(PlaqueB)), "found").Should().BeTrue(
            "portée null = aucun filtre, même sans une seule ligne dans user_vehicles");
        Champ<bool>(CorpsOk(await admin.Lookup("MAT-STOCK")), "found").Should().BeTrue();
    }

    // ═════════════════ B2 — « /api/alertemails » ═════════════════

    private static (AlertEmailsController Controleur, Mock<IMediator> Mediateur) Abonnements(ICurrentTenantService t)
    {
        var mediateur = new Mock<IMediator>();
        mediateur.Setup(m => m.Send(It.IsAny<GetAlertEmailsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AlertEmailDto>());
        mediateur.Setup(m => m.Send(It.IsAny<CreateAlertEmailCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        // UpdateAlertEmailCommand et DeleteAlertEmailCommand sont des IRequest SANS réponse :
        // la surcharge retenue est Send(object, CancellationToken), qui rend un Task nu.
        mediateur.Setup(m => m.Send(It.IsAny<UpdateAlertEmailCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<object?>(null));
        mediateur.Setup(m => m.Send(It.IsAny<DeleteAlertEmailCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<object?>(null));
        mediateur.Setup(m => m.Send(It.IsAny<TestAlertEmailCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediatR.Unit.Value);

        return (new AlertEmailsController(mediateur.Object, t) { ControllerContext = Contexte(t) }, mediateur);
    }

    private static void Refuse(IActionResult resultat) =>
        resultat.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);

    [Fact]
    public async Task Abonnement_aux_alertes_le_locataire_ne_peut_ni_lire_ni_inscrire_une_adresse()
    {
        var (controleur, mediateur) = Abonnements(Locataire());

        Refuse(await controleur.Create(new global::GisAPI.Controllers.CreateAlertEmailRequest("pirate@ailleurs.tn", "assurance")));
        Refuse(await controleur.GetAll(null));
        Refuse(await controleur.Update(1, new global::GisAPI.Controllers.UpdateAlertEmailRequest("pirate@ailleurs.tn", "assurance")));
        Refuse(await controleur.Delete(1));
        Refuse(await controleur.SendTest(1));

        mediateur.Verify(m => m.Send(It.IsAny<IRequest<int>>(), It.IsAny<CancellationToken>()), Times.Never,
            "la table alert_emails n'a aucune colonne véhicule : un abonnement porte sur TOUT le parc, "
            + "et un locataire de HERTZ recevait ainsi par e-mail les échéances des 305 véhicules des autres");
    }

    [Fact]
    public async Task Abonnement_aux_alertes_l_administrateur_sans_affectation_garde_la_main()
    {
        var (admin, mediateur) = Abonnements(AdminSansAffectation());

        (await admin.GetAll(null)).Should().BeOfType<OkObjectResult>();
        (await admin.Create(new global::GisAPI.Controllers.CreateAlertEmailRequest("gestion@hertz.tn", "assurance")))
            .Should().BeOfType<OkObjectResult>();
        (await admin.Update(1, new global::GisAPI.Controllers.UpdateAlertEmailRequest("gestion@hertz.tn", "assurance")))
            .Should().BeOfType<NoContentResult>();
        (await admin.Delete(1)).Should().BeOfType<NoContentResult>();
        (await admin.SendTest(1)).Should().BeOfType<OkObjectResult>();

        mediateur.Verify(m => m.Send(It.IsAny<CreateAlertEmailCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ═════════════════ B3 — « /api/companies » ═════════════════

    [Fact]
    public async Task Comptes_d_une_societe_le_locataire_n_a_plus_l_annuaire_ni_les_comptages()
    {
        using var parc = await ParcAsync();
        var locataire = Societes(parc.Pour(Locataire()), Locataire());

        (await locataire.GetCompanyUsers(Societe)).Result.Should().BeOfType<NotFoundResult>(
            "cette route sœur de « /api/users » publie nom, e-mail, téléphone, rôles, droits et "
            + "dernière connexion de TOUS les comptes, sans passer par la case Utilisateurs");
        (await locataire.GetCompanyStats(Societe)).Result.Should().BeOfType<NotFoundResult>();

        (await locataire.GetCompanyUsers(AutreSociete)).Result.Should().BeOfType<NotFoundResult>();
        (await locataire.GetCompanyStats(AutreSociete)).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Comptes_d_une_societe_l_administrateur_sans_affectation_garde_la_sienne()
    {
        using var parc = await ParcAsync();
        var admin = Societes(parc.Pour(AdminSansAffectation()), AdminSansAffectation());

        (await admin.GetCompanyUsers(Societe)).Result.Should().BeOfType<OkObjectResult>();
        (await admin.GetCompanyStats(Societe)).Result.Should().BeOfType<OkObjectResult>();

        (await admin.GetCompanyUsers(AutreSociete)).Result.Should().BeOfType<NotFoundResult>(
            "même un administrateur de société ne lit pas l'annuaire d'une AUTRE société");
        (await admin.GetCompanyStats(AutreSociete)).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Comptes_d_une_societe_l_administrateur_systeme_garde_la_vue_plate_forme()
    {
        using var parc = await ParcAsync();
        var systeme = Societes(parc.Pour(AdminSysteme()), AdminSysteme());

        (await systeme.GetCompanyUsers(AutreSociete)).Result.Should().BeOfType<OkObjectResult>();
        (await systeme.GetCompanyStats(AutreSociete)).Result.Should().BeOfType<OkObjectResult>();
    }

    // ═════════════════ B4 — « /api/tramonline/register/{mat} » ═════════════════

    [Fact]
    public async Task Enregistrement_d_un_MAT_le_locataire_ne_cree_plus_de_vehicule()
    {
        using var parc = await ParcAsync();
        var ctx = parc.Pour(Locataire());

        var resultat = await Tram(ctx, Locataire()).RegisterMat("MAT-NOUVEAU");

        resultat.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);

        var cree = await ctx.Vehicles.IgnoreQueryFilters().AnyAsync(v => v.Name.StartsWith("HTZ"));
        cree.Should().BeFalse("créer un véhicule est une opération de parc, pas un droit de locataire");
    }

    [Fact]
    public async Task Enregistrement_d_un_MAT_l_administrateur_cree_dans_SA_societe()
    {
        using var parc = await ParcAsync();
        var appelant = AdminAutreSociete();   // société 2, qui n'est PAS la première de la table
        var ctx = parc.Pour(appelant);

        var resultat = await Tram(ctx, appelant).RegisterMat("MAT-NOUVEAU");

        var reponse = resultat.Result.Should().BeOfType<OkObjectResult>().Which.Value
            .Should().BeOfType<global::GisAPI.Controllers.MatRegistrationResponse>().Subject;
        reponse.Created.Should().BeTrue();

        var vehicule = await ctx.Vehicles.IgnoreQueryFilters()
            .SingleAsync(v => v.Id == reponse.VehicleId);
        vehicule.CompanyId.Should().Be(AutreSociete,
            "la société d'accueil était Societes.OrderBy(Id).First() — la PREMIÈRE de la plate-forme — "
            + "et non celle de l'appelant : boîtier et véhicule atterrissaient chez un autre client");

        var boitier = await ctx.GpsDevices.IgnoreQueryFilters().SingleAsync(d => d.Id == reponse.DeviceId);
        boitier.CompanyId.Should().Be(AutreSociete);
    }

    // ═════════════════ B5 — « /api/device-events/{id}/acknowledge » ═════════════════

    [Fact]
    public async Task Acquittement_d_un_evenement_le_locataire_acquitte_le_sien()
    {
        using var parc = await ParcAsync();
        var ctx = parc.Pour(Locataire());

        var resultat = await Evenements(ctx, Locataire()).Acknowledge(1);

        resultat.Should().BeOfType<NoContentResult>(
            "la route lisait un claim « userId » que JwtService n'écrit jamais : elle rendait 401 à "
            + "TOUT LE MONDE, et le contrôle de portée ajouté juste au-dessus ne servait donc à rien");

        var evenement = await ctx.DeviceEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == 1);
        evenement.Acknowledged.Should().BeTrue();
        evenement.AcknowledgedBy.Should().Be(LocataireUserId);
    }

    [Fact]
    public async Task Acquittement_d_un_evenement_hors_portee_reste_un_404()
    {
        using var parc = await ParcAsync();
        var ctx = parc.Pour(Locataire());
        var locataire = Evenements(ctx, Locataire());

        (await locataire.Acknowledge(2)).Should().BeOfType<NotFoundResult>(
            "acquitter l'événement d'un véhicule hors portée, c'est l'effacer de l'écran de son vrai locataire");
        (await locataire.Acknowledge(3)).Should().BeOfType<NotFoundResult>(
            "un événement de boîtier en stock n'entre dans la portée de personne");

        var intacts = await ctx.DeviceEvents.IgnoreQueryFilters()
            .Where(e => e.Id == 2 || e.Id == 3).ToListAsync();
        intacts.Should().OnlyContain(e => !e.Acknowledged);
    }

    [Fact]
    public async Task Acquittement_d_un_evenement_l_administrateur_sans_affectation_acquitte_tout()
    {
        using var parc = await ParcAsync();
        var ctx = parc.Pour(AdminSansAffectation());
        var admin = Evenements(ctx, AdminSansAffectation());

        (await admin.Acknowledge(2)).Should().BeOfType<NoContentResult>();
        (await admin.Acknowledge(3)).Should().BeOfType<NoContentResult>();

        var evenement = await ctx.DeviceEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == 2);
        evenement.AcknowledgedBy.Should().Be(AdminUserId);
    }
}
