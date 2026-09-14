using System.Reflection;
using FluentAssertions;
using GisAPI.Application.Features.Admin.Brands;
using GisAPI.Attributes;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Référentiel marques / modèles (recette TN du 14/09/2026). Deux constats : l'écran /admin
/// n'arrivait plus à créer (mutations sur /api/brands parties sans admin_token → 401 muet),
/// et ces mêmes mutations étaient ouvertes à tout client connecté. Elles vivent désormais
/// sous /api/admin/brands, sans doublon ni espaces parasites (« ATECA  » en base TN).
/// </summary>
public class BrandCatalogTests
{
    // ── Nettoyage des saisies ────────────────────────────────────────────────────

    [Theory]
    [InlineData("  ATECA  ", "ATECA")]
    [InlineData("CUPRA ", "CUPRA")]
    [InlineData(" Land   Cruiser\t", "Land Cruiser")]
    [InlineData("T.CROSS", "T.CROSS")]
    [InlineData(null, "")]
    public void Le_nom_est_debarrasse_de_ses_espaces_parasites(string? raw, string expected)
        => BrandCatalogRules.NormalizeName(raw).Should().Be(expected);

    [Theory]
    [InlineData("ATECA  ", "ateca")]
    [InlineData("Land Cruiser", "LAND   CRUISER ")]
    [InlineData("kushaq", "KUSHAQ")]
    public void Deux_noms_qui_ne_different_que_par_la_casse_ou_les_espaces_sont_homonymes(string a, string b)
        => BrandCatalogRules.SameName(a, b).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public void Un_nom_vide_est_refuse(string? raw)
    {
        BrandCatalogRules.TryNormalizeName(raw, "de la marque", out _, out var error).Should().BeFalse();
        error.Should().Be("Le nom de la marque est obligatoire.");
    }

    [Fact]
    public void Un_nom_plus_long_que_la_colonne_est_refuse()
    {
        BrandCatalogRules.TryNormalizeName(new string('A', BrandCatalogRules.MaxNameLength + 1), "du modèle", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("100 caractères");
    }

    [Theory]
    [InlineData(" Citadine ", "citadine")]
    [InlineData("SUV", "suv")]
    [InlineData("utilitaire", "utilitaire")]
    [InlineData("Camion", "camion")]
    [InlineData("Autre", "other")]
    [InlineData("van", "van")]           // valeurs héritées du jeu initial, majoritaires en base TN
    [InlineData("Hatchback", "hatchback")]
    [InlineData("pickup", "pickup")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Le_type_de_vehicule_est_stocke_comme_les_donnees_existantes(string? raw, string? expected)
    {
        BrandCatalogRules.TryNormalizeVehicleType(raw, out var type, out var error).Should().BeTrue();
        type.Should().Be(expected);
        error.Should().BeNull();
    }

    [Fact]
    public void Un_type_de_vehicule_inconnu_est_refuse()
    {
        BrandCatalogRules.TryNormalizeVehicleType("voiture volante", out var type, out var error).Should().BeFalse();
        type.Should().BeNull();
        error.Should().Contain("voiture volante");
    }

    // ── Marques ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Une_marque_est_creee_avec_un_nom_nettoye()
    {
        using var db = TestDbContextFactory.Create();
        var result = await Handler(db).Handle(new CreateBrandCommand("  SEAT   Cupra ", "  "), default);

        result.Status.Should().Be(BrandCatalogStatus.Created);
        var brand = await db.Brands.SingleAsync();
        brand.Id.Should().Be(result.Id);
        brand.Name.Should().Be("SEAT Cupra");
        brand.LogoUrl.Should().BeNull();
        brand.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("seat")]
    [InlineData("  SEAT  ")]
    [InlineData("Seat")]
    public async Task Une_marque_active_du_meme_nom_est_refusee(string name)
    {
        using var db = TestDbContextFactory.Create();
        var seat = await AddBrand(db, "SEAT");

        var result = await Handler(db).Handle(new CreateBrandCommand(name, null), default);

        result.Status.Should().Be(BrandCatalogStatus.Conflict);
        result.Id.Should().Be(seat.Id);
        result.Message.Should().Be("La marque « SEAT » existe déjà.");
        (await db.Brands.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Une_marque_inactive_du_meme_nom_est_reactivee_au_lieu_d_etre_dupliquee()
    {
        using var db = TestDbContextFactory.Create();
        var skoda = await AddBrand(db, "Skoda ", active: false);

        var result = await Handler(db).Handle(new CreateBrandCommand("SKODA", "/logos/skoda.png"), default);

        result.Status.Should().Be(BrandCatalogStatus.Reactivated);
        result.Id.Should().Be(skoda.Id);
        result.Message.Should().Contain("réactivée");
        result.Message.Should().Contain("« Skoda »");
        var brand = await db.Brands.SingleAsync();
        brand.IsActive.Should().BeTrue();
        brand.Name.Should().Be("Skoda ", "le nom stocké reste celui que portent les véhicules");
        brand.LogoUrl.Should().Be("/logos/skoda.png");
    }

    [Fact]
    public async Task Une_marque_au_nom_vide_n_est_pas_creee()
    {
        using var db = TestDbContextFactory.Create();
        var result = await Handler(db).Handle(new CreateBrandCommand("   ", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Invalid);
        result.Message.Should().Be("Le nom de la marque est obligatoire.");
        (await db.Brands.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Renommer_une_marque_vers_un_nom_deja_pris_est_refuse()
    {
        using var db = TestDbContextFactory.Create();
        await AddBrand(db, "Peugeot");
        var citroen = await AddBrand(db, "Citroën");

        var result = await Handler(db).Handle(new UpdateBrandCommand(citroen.Id, " PEUGEOT", null, true), default);

        result.Status.Should().Be(BrandCatalogStatus.Conflict);
        result.Message.Should().Contain("déjà pris");
        (await db.Brands.AsNoTracking().SingleAsync(b => b.Id == citroen.Id)).Name.Should().Be("Citroën");
    }

    [Fact]
    public async Task Renommer_une_marque_en_changeant_seulement_la_casse_est_permis()
    {
        using var db = TestDbContextFactory.Create();
        var isuzu = await AddBrand(db, "ISUZU");

        var result = await Handler(db).Handle(new UpdateBrandCommand(isuzu.Id, "Isuzu ", "/logos/isuzu.png", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Updated);
        var brand = await db.Brands.AsNoTracking().SingleAsync();
        brand.Name.Should().Be("Isuzu");
        brand.IsActive.Should().BeTrue("IsActive absent = inchangé");
    }

    [Fact]
    public async Task Renommer_une_marque_vers_le_nom_d_une_marque_inactive_est_permis()
    {
        using var db = TestDbContextFactory.Create();
        await AddBrand(db, "Lada", active: false);
        var dacia = await AddBrand(db, "Dacia");

        var result = await Handler(db).Handle(new UpdateBrandCommand(dacia.Id, "LADA", null, null), default);

        result.Status.Should().Be(BrandCatalogStatus.Updated);
    }

    [Fact]
    public async Task Desactiver_une_marque_la_garde_en_base()
    {
        using var db = TestDbContextFactory.Create();
        var man = await AddBrand(db, "MAN");

        var result = await Handler(db).Handle(new DeactivateBrandCommand(man.Id), default);

        result.Status.Should().Be(BrandCatalogStatus.Deactivated);
        (await db.Brands.AsNoTracking().SingleAsync()).IsActive.Should().BeFalse();
        (await Handler(db).Handle(new DeactivateBrandCommand(999), default)).Status.Should().Be(BrandCatalogStatus.NotFound);
    }

    // ── Modèles ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_modele_est_cree_avec_nom_et_type_normalises()
    {
        using var db = TestDbContextFactory.Create();
        var seat = await AddBrand(db, "SEAT");

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(seat.Id, " ARONA  ", "Citadine"), default);

        result.Status.Should().Be(BrandCatalogStatus.Created);
        var model = await db.VehicleModels.SingleAsync();
        model.Name.Should().Be("ARONA");
        model.VehicleType.Should().Be("citadine");
        model.BrandId.Should().Be(seat.Id);
    }

    [Theory]
    [InlineData("ateca")]
    [InlineData("ATECA")]
    [InlineData("  Ateca ")]
    public async Task Un_modele_actif_du_meme_nom_pour_la_meme_marque_est_refuse(string name)
    {
        using var db = TestDbContextFactory.Create();
        var seat = await AddBrand(db, "SEAT");
        var ateca = await AddModel(db, seat.Id, "ATECA  "); // tel qu'en base TN (n° 107)

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(seat.Id, name, "suv"), default);

        result.Status.Should().Be(BrandCatalogStatus.Conflict);
        result.Id.Should().Be(ateca.Id);
        result.Message.Should().Be("Le modèle « ATECA » existe déjà pour la marque « SEAT ».");
        (await db.VehicleModels.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Le_meme_nom_de_modele_sous_une_autre_marque_est_permis()
    {
        using var db = TestDbContextFactory.Create();
        var renault = await AddBrand(db, "Renault");
        var dacia = await AddBrand(db, "Dacia");
        await AddModel(db, renault.Id, "Duster");

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(dacia.Id, "Duster", "suv"), default);

        result.Status.Should().Be(BrandCatalogStatus.Created);
        (await db.VehicleModels.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Un_modele_inactif_du_meme_nom_est_reactive_au_lieu_d_etre_duplique()
    {
        using var db = TestDbContextFactory.Create();
        var vw = await AddBrand(db, "Volkswagen");
        var golf = await AddModel(db, vw.Id, "Golf", "hatchback", active: false);

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(vw.Id, "GOLF", "citadine"), default);

        result.Status.Should().Be(BrandCatalogStatus.Reactivated);
        result.Id.Should().Be(golf.Id);
        result.Message.Should().Contain("réactivé");
        var model = await db.VehicleModels.SingleAsync();
        model.IsActive.Should().BeTrue();
        model.VehicleType.Should().Be("citadine");
    }

    [Fact]
    public async Task Parmi_plusieurs_homonymes_inactifs_le_plus_ancien_est_reactive()
    {
        using var db = TestDbContextFactory.Create();
        var skoda = await AddBrand(db, "Skoda");
        var first = await AddModel(db, skoda.Id, "kushaq", active: false);
        await AddModel(db, skoda.Id, "KUSHAQ", active: false);

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(skoda.Id, "Kushaq", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Reactivated);
        result.Id.Should().Be(first.Id);
        (await db.VehicleModels.CountAsync(m => m.IsActive)).Should().Be(1);
    }

    [Fact]
    public async Task Un_modele_au_nom_vide_ou_au_type_inconnu_n_est_pas_cree()
    {
        using var db = TestDbContextFactory.Create();
        var seat = await AddBrand(db, "SEAT");

        var empty = await Handler(db).Handle(new CreateVehicleModelCommand(seat.Id, "  ", "suv"), default);
        var badType = await Handler(db).Handle(new CreateVehicleModelCommand(seat.Id, "Ibiza", "fusée"), default);
        var noBrand = await Handler(db).Handle(new CreateVehicleModelCommand(999, "Ibiza", "citadine"), default);

        empty.Status.Should().Be(BrandCatalogStatus.Invalid);
        empty.Message.Should().Be("Le nom du modèle est obligatoire.");
        badType.Status.Should().Be(BrandCatalogStatus.Invalid);
        noBrand.Status.Should().Be(BrandCatalogStatus.NotFound);
        (await db.VehicleModels.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Renommer_un_modele_vers_un_nom_deja_pris_dans_la_marque_est_refuse()
    {
        using var db = TestDbContextFactory.Create();
        var toyota = await AddBrand(db, "Toyota");
        await AddModel(db, toyota.Id, "Yaris");
        var corolla = await AddModel(db, toyota.Id, "Corolla");

        var result = await Handler(db).Handle(new UpdateVehicleModelCommand(corolla.Id, "yaris ", "citadine", true), default);

        result.Status.Should().Be(BrandCatalogStatus.Conflict);
        result.Message.Should().Contain("déjà pris");
        (await db.VehicleModels.AsNoTracking().SingleAsync(m => m.Id == corolla.Id)).Name.Should().Be("Corolla");
    }

    [Fact]
    public async Task Un_doublon_historique_reste_modifiable_tant_qu_il_n_est_pas_renomme()
    {
        // Base TN : « Hiace » n° 5 et n° 62, tous deux actifs. Corriger le type du second
        // ne crée aucun doublon supplémentaire : l'enregistrement doit passer.
        using var db = TestDbContextFactory.Create();
        var toyota = await AddBrand(db, "Toyota");
        await AddModel(db, toyota.Id, "Hiace", "van");
        var second = await AddModel(db, toyota.Id, "Hiace", "van");

        var result = await Handler(db).Handle(new UpdateVehicleModelCommand(second.Id, "HIACE", "utilitaire", true), default);

        result.Status.Should().Be(BrandCatalogStatus.Updated);
        var model = await db.VehicleModels.AsNoTracking().SingleAsync(m => m.Id == second.Id);
        model.Name.Should().Be("HIACE");
        model.VehicleType.Should().Be("utilitaire");
    }

    [Fact]
    public async Task Reactiver_un_modele_par_modification_alors_qu_un_homonyme_est_actif_est_refuse()
    {
        using var db = TestDbContextFactory.Create();
        var peugeot = await AddBrand(db, "Peugeot");
        await AddModel(db, peugeot.Id, "208");
        var old = await AddModel(db, peugeot.Id, "208", active: false); // n° 68 en base TN

        var result = await Handler(db).Handle(new UpdateVehicleModelCommand(old.Id, "208", null, true), default);

        result.Status.Should().Be(BrandCatalogStatus.Conflict);
        (await db.VehicleModels.AsNoTracking().SingleAsync(m => m.Id == old.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Modifier_un_modele_sans_IsActive_garde_son_etat_et_refuse_un_nom_vide()
    {
        using var db = TestDbContextFactory.Create();
        var kia = await AddBrand(db, "KIA");
        var rio = await AddModel(db, kia.Id, "RIO", "citadine");

        var ok = await Handler(db).Handle(new UpdateVehicleModelCommand(rio.Id, "Rio", "citadine", null), default);
        var empty = await Handler(db).Handle(new UpdateVehicleModelCommand(rio.Id, " ", "citadine", null), default);

        ok.Status.Should().Be(BrandCatalogStatus.Updated);
        empty.Status.Should().Be(BrandCatalogStatus.Invalid);
        var model = await db.VehicleModels.AsNoTracking().SingleAsync();
        model.Name.Should().Be("Rio");
        model.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Desactiver_un_modele_le_garde_en_base()
    {
        using var db = TestDbContextFactory.Create();
        var kia = await AddBrand(db, "KIA");
        var picanto = await AddModel(db, kia.Id, "PICANTO");

        var result = await Handler(db).Handle(new DeactivateVehicleModelCommand(picanto.Id), default);

        result.Status.Should().Be(BrandCatalogStatus.Deactivated);
        (await db.VehicleModels.AsNoTracking().SingleAsync()).IsActive.Should().BeFalse();
    }

    // ── Lien texte véhicule → modèle ─────────────────────────────────────────────
    // vehicles.model recopie le nom du modèle, et le formulaire véhicule le retrouve par
    // égalité sans trim (base TN : véhicules 102 « ATECA  », 84 et 116 « CUPRA »…).

    [Theory]
    [InlineData("ATECA  ", "ATECA", "ATECA  ")]      // enregistré sans renommer : nom stocké intact
    [InlineData(" Landtrek", "Landtrek", " Landtrek")]
    [InlineData("ATECA  ", "Ateca", "Ateca")]        // vrai renommage (casse) : nom nettoyé
    [InlineData("Tiguan ", "Tiguan Allspace", "Tiguan Allspace")]
    [InlineData("RIO", "RIO", "RIO")]
    public void Le_nom_stocke_n_est_reecrit_qu_en_cas_de_vrai_renommage(string current, string normalizedNew, string expected)
        => BrandCatalogRules.NameToStore(current, normalizedNew).Should().Be(expected);

    [Theory]
    [InlineData("ATECA  ")]   // valeur renvoyée telle quelle par l'écran
    [InlineData("ATECA")]
    [InlineData(" ATECA ")]
    public async Task Modifier_le_type_d_un_modele_a_espaces_parasites_ne_touche_pas_a_son_nom(string submitted)
    {
        using var db = TestDbContextFactory.Create();
        var seat = await AddBrand(db, "SEAT");
        var ateca = await AddModel(db, seat.Id, "ATECA  ", "suv"); // n° 107 en base TN

        var result = await Handler(db).Handle(new UpdateVehicleModelCommand(ateca.Id, submitted, "utilitaire", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Updated);
        result.Message.Should().Be("Le modèle « ATECA » a été enregistré.");
        var model = await db.VehicleModels.AsNoTracking().SingleAsync();
        model.Name.Should().Be("ATECA  ");
        model.VehicleType.Should().Be("utilitaire");
    }

    [Fact]
    public async Task Modifier_le_logo_d_une_marque_a_espaces_parasites_ne_touche_pas_a_son_nom()
    {
        using var db = TestDbContextFactory.Create();
        var kia = await AddBrand(db, " KIA ");

        var result = await Handler(db).Handle(new UpdateBrandCommand(kia.Id, "KIA", "/logos/kia.png", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Updated);
        var brand = await db.Brands.AsNoTracking().SingleAsync();
        brand.Name.Should().Be(" KIA ");
        brand.LogoUrl.Should().Be("/logos/kia.png");
    }

    [Fact]
    public async Task Un_modele_reactive_garde_le_nom_que_portent_ses_vehicules()
    {
        using var db = TestDbContextFactory.Create();
        var peugeot = await AddBrand(db, "Peugeot");
        var landtrek = await AddModel(db, peugeot.Id, " Landtrek", "pickup", active: false);

        var result = await Handler(db).Handle(new CreateVehicleModelCommand(peugeot.Id, "LANDTREK", null), default);

        result.Status.Should().Be(BrandCatalogStatus.Reactivated);
        result.Id.Should().Be(landtrek.Id);
        result.Message.Should().Contain("« Landtrek »");
        var model = await db.VehicleModels.AsNoTracking().SingleAsync();
        model.Name.Should().Be(" Landtrek");
        model.VehicleType.Should().Be("pickup", "type absent = inchangé");
    }

    [Theory]
    [InlineData("Hiace", true, "HIACE", true, false)]    // même clé, déjà actif : pas de nouveau doublon possible
    [InlineData("Hiace", true, "Hilux", true, true)]     // renommage
    [InlineData("Hiace", false, "Hiace", true, true)]    // réactivation
    [InlineData("Hiace", true, "Hilux", false, false)]   // désactivé : ne peut rien dupliquer
    public void Le_controle_d_homonyme_ne_vise_que_ce_qui_peut_creer_un_doublon_actif(
        string current, bool currentlyActive, string next, bool willBeActive, bool expected)
        => BrandCatalogCommandHandler.CouldCreateActiveHomonym(current, currentlyActive, next, willBeActive).Should().Be(expected);

    // ── Exposition HTTP ──────────────────────────────────────────────────────────

    [Fact]
    public void BrandsController_n_expose_plus_que_des_lectures()
    {
        var actions = typeof(BrandsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        actions.Should().NotBeEmpty();
        foreach (var action in actions)
        {
            // Une méthode publique sans attribut de verbe répondrait à TOUS les verbes.
            var verbs = action.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).ToList();
            verbs.Should().Equal(new[] { "GET" }, $"{action.Name} ne doit répondre qu'en lecture");
        }
    }

    [Fact]
    public void Aucune_route_api_brands_n_accepte_une_mutation()
    {
        var offenders = AllRoutes()
            .Where(r => r.Template.StartsWith("api/brands", StringComparison.OrdinalIgnoreCase) && r.Verb != "GET")
            .Select(r => $"{r.Verb} {r.Template} ({r.Action})")
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Les_mutations_sont_sous_api_admin_brands_et_reservees_a_l_administrateur_systeme()
    {
        var controller = typeof(AdminBrandsController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/brands");
        controller.GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull();
        controller.GetCustomAttribute<RequireAdminAttribute>().Should().NotBeNull();

        var routes = AllRoutes().Where(r => r.Controller == controller).Select(r => $"{r.Verb} {r.Template}").ToList();
        routes.Should().BeEquivalentTo(new[]
        {
            "POST api/admin/brands",
            "PUT api/admin/brands/{id:int}",
            "DELETE api/admin/brands/{id:int}",
            "POST api/admin/brands/{brandId:int}/models",
            "PUT api/admin/brands/models/{id:int}",
            "DELETE api/admin/brands/models/{id:int}",
        });
    }

    [Theory]
    [InlineData("/api/admin/brands", "POST")]
    [InlineData("/api/admin/brands/26", "PUT")]
    [InlineData("/api/admin/brands/26", "DELETE")]
    [InlineData("/api/admin/brands/26/models", "POST")]
    [InlineData("/api/admin/brands/models/127", "PUT")]
    [InlineData("/api/admin/brands/models/127", "DELETE")]
    public void PermissionMiddleware_soumet_api_admin_brands_a_la_garde_systeme(string path, string method)
        => PermissionMiddleware.ClassifyRoute(path, method).Should().Be(PermissionMiddleware.RouteGate.SystemAdmin);

    [Theory]
    [InlineData("/api/brands")]
    [InlineData("/api/brands/26")]
    [InlineData("/api/brands/26/models")]
    public void Les_lectures_api_brands_restent_ouvertes_aux_clients(string path)
        => PermissionMiddleware.ClassifyRoute(path, "GET").Should().Be(PermissionMiddleware.RouteGate.AlwaysOpen);

    [Theory]
    [InlineData(BrandCatalogStatus.Created, 201)]
    [InlineData(BrandCatalogStatus.Reactivated, 200)]
    [InlineData(BrandCatalogStatus.Updated, 200)]
    [InlineData(BrandCatalogStatus.Deactivated, 200)]
    [InlineData(BrandCatalogStatus.Invalid, 400)]
    [InlineData(BrandCatalogStatus.NotFound, 404)]
    [InlineData(BrandCatalogStatus.Conflict, 409)]
    public void Chaque_issue_a_son_statut_HTTP_et_porte_le_message(BrandCatalogStatus status, int httpStatus)
    {
        var response = new AdminBrandsController(null!).ToResponse(new BrandCatalogResult(status, "message", 7));

        var result = response.Should().BeAssignableTo<ObjectResult>().Subject;
        result.StatusCode.Should().Be(httpStatus);
        var body = result.Value.Should().BeOfType<AdminBrandCatalogResponse>().Subject;
        body.Message.Should().Be("message");
        body.Reactivated.Should().Be(status == BrandCatalogStatus.Reactivated);
    }

    // ── Outils ───────────────────────────────────────────────────────────────────

    private static BrandCatalogCommandHandler Handler(TestGisDbContext db) => new(db);

    private static async Task<Brand> AddBrand(TestGisDbContext db, string name, bool active = true)
    {
        var brand = new Brand { Name = name, IsActive = active };
        db.Brands.Add(brand);
        await db.SaveChangesAsync();
        return brand;
    }

    private static async Task<VehicleModel> AddModel(TestGisDbContext db, int brandId, string name, string? type = null, bool active = true)
    {
        var model = new VehicleModel { BrandId = brandId, Name = name, VehicleType = type, IsActive = active };
        db.VehicleModels.Add(model);
        await db.SaveChangesAsync();
        return model;
    }

    private sealed record RouteInfo(Type Controller, string Action, string Verb, string Template);

    /// <summary>Routes par attributs de tous les contrôleurs de l'API : préfixe de classe + gabarit de verbe.</summary>
    private static IEnumerable<RouteInfo> AllRoutes()
    {
        var controllers = typeof(BrandsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        foreach (var controller in controllers)
        {
            var name = controller.Name.EndsWith("Controller") ? controller.Name[..^"Controller".Length] : controller.Name;
            var prefixes = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template.Replace("[controller]", name, StringComparison.OrdinalIgnoreCase))
                .DefaultIfEmpty(string.Empty)
                .ToList();

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var verbAttribute in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    foreach (var verb in verbAttribute.HttpMethods)
                    {
                        foreach (var prefix in prefixes)
                        {
                            var template = verbAttribute.Template switch
                            {
                                null or "" => prefix,
                                var t when t.StartsWith("~/") => t[2..],
                                var t when t.StartsWith("/") => t[1..],
                                var t => string.IsNullOrEmpty(prefix) ? t : $"{prefix}/{t}",
                            };
                            yield return new RouteInfo(controller, action.Name, verb, template.Trim('/'));
                        }
                    }
                }
            }
        }
    }
}
