using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using FicheEntretienDto = global::GisAPI.Controllers.FicheEntretienDto;
using MaintenanceController = global::GisAPI.Controllers.MaintenanceController;
using VehiculeEntretienDto = global::GisAPI.Controllers.VehiculeEntretienDto;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// « /api/maintenance » — incident HERTZ (société 4), lot B.
///
/// Ce contrôleur « épais » (accès directs au contexte, aucun MediatR) avait échappé aux
/// trois passes : il ne filtrait QUE par société, alors que ses frères
/// (GetVehicleMaintenanceQueryHandler, MaintenanceAlertsQueryHandlers,
/// VehicleMaintenanceCommandHandlers) appliquent tous <c>VehicleScope</c>. Chez un
/// LOUEUR, un compte restreint ayant la case Entretien — 17 comptes non administrateurs
/// en production, dont 7 chez SICOAC — lisait les fiches de tout le parc (entité Vehicle
/// complète et coûts compris), et pouvait saisir un entretien sur le véhicule loué à un
/// autre client : SON compteur avançait et une dépense lui était imputée.
///
/// Les tests appellent les MÉTHODES DE PRODUCTION du vrai contrôleur contre un
/// <see cref="GisDbContext"/> en mémoire, avec une identité ÉMISE PUIS VALIDÉE par le
/// vrai JwtService (<see cref="JetonDeProduction"/>) : aucun claim fabriqué à la main.
///
/// PIÈGE COUVERT — la portée a TROIS états :
///   • <c>null</c>    → administrateur, AUCUN filtre (tout le parc) ;
///   • liste non vide → ses véhicules ;
///   • liste VIDE     → non-administrateur sans affectation, il ne voit RIEN.
/// L'administrateur SANS aucune ligne dans user_vehicles (utilisateur 11 de HERTZ) doit
/// continuer à tout voir et tout saisir : d'où un TEST JUMEAU « admin » sur chaque route.
/// </summary>
public class MaintenanceControllerPorteeTests
{
    private const int CompanyId = 1;
    private const int AutreSociete = 2;

    /// <summary>Le locataire restreint (calque de Kap Pharma, utilisateur 58) : véhicule A seul.</summary>
    private const int LocataireUserId = 58;

    /// <summary>L'administrateur de la société SANS aucune affectation (utilisateur 11 de HERTZ).</summary>
    private const int AdminUserId = 11;

    /// <summary>Un non-administrateur sans aucune affectation : il ne voit RIEN.</summary>
    private const int SansAffectationUserId = 99;

    private const int VehiculeA = 1;   // loué au locataire
    private const int VehiculeB = 2;   // loué à un autre client
    private const int VehiculeAutreSociete = 9;

    private const int KmA = 10_000;
    private const int KmB = 50_000;

    private const int FicheA = 101;   // entretien planifié du véhicule A
    private const int FicheB = 102;   // entretien planifié du véhicule B
    private const int FicheAutreSociete = 109;
    private const int Inexistante = 9_999;

    private static readonly DateTime Bientot = DateTime.UtcNow.Date.AddDays(5);

    // ───────────────── Appelants ─────────────────

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        m.Setup(x => x.IsSystemAdmin).Returns(roles.Contains("system_admin"));
        return m.Object;
    }

    private static ICurrentTenantService Locataire() => Tenant(LocataireUserId, "Operateur");
    private static ICurrentTenantService AdminSansAffectation() => Tenant(AdminUserId, "company_admin");
    private static ICurrentTenantService NonAdminSansAffectation() => Tenant(SansAffectationUserId, "Operateur");

    // ───────────────── Contexte et jeu de données ─────────────────

    /// <summary>Vrai GisDbContext en mémoire : ce contrôleur filtre DANS ses actions.</summary>
    private sealed class ContexteControleur : GisDbContext
    {
        public ContexteControleur(string nomBase)
            : base(new DbContextOptionsBuilder<GisDbContext>()
                    .UseInMemoryDatabase(nomBase, b => b.EnableNullChecks(false))
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
    /// Base partagée par un contexte « requête » (celui du contrôleur, vierge de tout suivi
    /// comme en production) et un contexte « contrôle » ouvert APRÈS l'appel, qui relit ce
    /// qui a réellement été écrit en base de test.
    /// </summary>
    private sealed class Parc : IDisposable
    {
        private readonly string _nom = Guid.NewGuid().ToString();
        public ContexteControleur Requete { get; }

        private Parc() => Requete = new ContexteControleur(_nom);

        public ContexteControleur Controle() => new(_nom);

        public void Dispose() => Requete.Dispose();

        /// <summary>
        /// Société 1 : véhicule A (loué au locataire, 10 000 km) et véhicule B (loué à un
        /// autre client, 50 000 km), chacun avec un entretien planifié sous un mois et une
        /// pièce. Plus une fiche d'une autre société, qui ne doit jamais apparaître.
        /// Seul A est affecté au locataire ; l'administrateur n'a AUCUNE ligne dans
        /// user_vehicles — le cas piège à ne pas casser.
        /// </summary>
        public static async Task<Parc> CreerAsync()
        {
            var parc = new Parc();
            await using var ctx = parc.Controle();

            ctx.Vehicles.AddRange(
                new Vehicle { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId, Mileage = KmA, Status = "active" },
                new Vehicle { Id = VehiculeB, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId, Mileage = KmB, Status = "active" },
                new Vehicle { Id = VehiculeAutreSociete, Name = "Autre société", Plate = "999 TU 9", CompanyId = AutreSociete, Mileage = 1, Status = "active" });

            ctx.MaintenanceRecords.AddRange(
                new MaintenanceRecord
                {
                    Id = FicheA, VehicleId = VehiculeA, CompanyId = CompanyId, Type = "scheduled", Description = "Vidange A",
                    Date = Bientot, Status = "scheduled", LaborCost = 40, PartsCost = 60, TotalCost = 100, InvoiceNumber = "FA-1"
                },
                new MaintenanceRecord
                {
                    Id = FicheB, VehicleId = VehiculeB, CompanyId = CompanyId, Type = "scheduled", Description = "Vidange B",
                    Date = Bientot, Status = "scheduled", LaborCost = 300, PartsCost = 200, TotalCost = 500, InvoiceNumber = "FB-2",
                    ServiceProvider = "Garage de l'autre client"
                },
                new MaintenanceRecord
                {
                    Id = FicheAutreSociete, VehicleId = VehiculeAutreSociete, CompanyId = AutreSociete, Type = "scheduled",
                    Description = "Autre société", Date = Bientot, Status = "scheduled", LaborCost = 1, PartsCost = 1, TotalCost = 2
                });

            ctx.MaintenanceParts.AddRange(
                new MaintenancePart { Id = 1, MaintenanceRecordId = FicheA, Name = "Filtre A", Quantity = 1, UnitCost = 60, TotalCost = 60 },
                new MaintenancePart { Id = 2, MaintenanceRecordId = FicheB, Name = "Filtre B", Quantity = 2, UnitCost = 100, TotalCost = 200 });

            ctx.UserVehicles.AddRange(
                new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = VehiculeA },
                new UserVehicle { Id = 2, UserId = 77, VehicleId = VehiculeB });

            await ctx.SaveChangesAsync();
            return parc;
        }
    }

    /// <summary>
    /// Identité de l'appelant, ÉMISE ET VALIDÉE par le code de production : le contrôleur
    /// lit « companyId » dans le jeton, comme en ligne.
    /// </summary>
    private static MaintenanceController Entretiens(Parc parc, ICurrentTenantService t) =>
        new(parc.Requete, t) { ControllerContext = JetonDeProduction.Contexte(t, CompanyId) };

    private static T Contenu<T>(ActionResult? resultat)
    {
        resultat.Should().BeOfType<OkObjectResult>();
        return (T)((OkObjectResult)resultat!).Value!;
    }

    private static MaintenanceRecord Saisie(int vehicleId, int km) => new()
    {
        VehicleId = vehicleId,
        Type = "scheduled",
        Description = "Vidange saisie depuis les Dépenses",
        MileageAtService = km,
        Date = DateTime.UtcNow.Date,
        Status = "completed",
        LaborCost = 100,
        PartsCost = 50,
        InvoiceNumber = "SAISIE-1"
    };

    /// <summary>Ce qui a RÉELLEMENT été écrit en base de test, relu par un contexte neuf.</summary>
    private static async Task<(int km, int couts, int fiches)> EtatDuVehiculeAsync(Parc parc, int vehicleId)
    {
        await using var ctx = parc.Controle();
        var km = await ctx.Vehicles.IgnoreQueryFilters().Where(v => v.Id == vehicleId).Select(v => v.Mileage).SingleAsync();
        var couts = await ctx.VehicleCosts.IgnoreQueryFilters().CountAsync(c => c.VehicleId == vehicleId);
        var fiches = await ctx.MaintenanceRecords.IgnoreQueryFilters().CountAsync(m => m.VehicleId == vehicleId);
        return (km, couts, fiches);
    }

    // ───────────────── GET /api/maintenance ─────────────────

    [Fact]
    public async Task Liste_le_locataire_ne_voit_que_les_fiches_de_son_vehicule()
    {
        using var parc = await Parc.CreerAsync();
        var locataire = Entretiens(parc, Locataire());

        var fiches = Contenu<List<FicheEntretienDto>>((await locataire.GetMaintenanceRecords()).Result);
        fiches.Select(f => f.VehicleId).Should().Equal(new[] { VehiculeA },
            "sans vehicleId, la liste rendait les entretiens, coûts et prestataires de TOUT le parc du loueur");

        var ciblee = Contenu<List<FicheEntretienDto>>((await locataire.GetMaintenanceRecords(VehiculeB)).Result);
        ciblee.Should().BeEmpty("le filtre vehicleId ne fait qu'INTERSECTER la portée, il ne l'élargit jamais");
    }

    [Fact]
    public async Task Liste_l_administrateur_sans_affectation_voit_tout_le_parc()
    {
        using var parc = await Parc.CreerAsync();

        var fiches = Contenu<List<FicheEntretienDto>>(
            (await Entretiens(parc, AdminSansAffectation()).GetMaintenanceRecords()).Result);

        fiches.Select(f => f.Id).Should().BeEquivalentTo(new[] { FicheA, FicheB },
            "portée null = aucun filtre, même sans une seule ligne dans user_vehicles — mais jamais l'autre société");
    }

    [Fact]
    public async Task Liste_un_non_administrateur_sans_affectation_ne_voit_rien()
    {
        using var parc = await Parc.CreerAsync();
        var sansAffectation = Entretiens(parc, NonAdminSansAffectation());

        Contenu<List<FicheEntretienDto>>((await sansAffectation.GetMaintenanceRecords()).Result)
            .Should().BeEmpty("liste VIDE = il ne voit RIEN, surtout pas l'absence de filtre");
        Contenu<List<FicheEntretienDto>>((await sansAffectation.GetUpcomingMaintenance()).Result)
            .Should().BeEmpty();
    }

    // ───────────────── GET /api/maintenance/upcoming ─────────────────

    [Fact]
    public async Task A_venir_le_locataire_ne_voit_que_son_vehicule_et_plus_l_entite_Vehicle_brute()
    {
        using var parc = await Parc.CreerAsync();

        var fiches = Contenu<List<FicheEntretienDto>>(
            (await Entretiens(parc, Locataire()).GetUpcomingMaintenance()).Result);

        fiches.Select(f => f.VehicleId).Should().Equal(new[] { VehiculeA },
            "la route appelée par api.service.ts publiait l'entité Vehicle COMPLÈTE de tout le parc");

        var fiche = fiches.Single();
        fiche.Vehicle.Should().BeEquivalentTo(new VehiculeEntretienDto { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = "111 TU 1" });
        fiche.Description.Should().Be("Vidange A");
        fiche.TotalCost.Should().Be(100);
        fiche.Parts.Select(p => p.Name).Should().Equal("Filtre A");

        typeof(VehiculeEntretienDto).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { "Id", "Name", "Plate" },
                "le résumé du véhicule ne porte ni kilométrage, ni échéances, ni boîtier");
    }

    [Fact]
    public async Task A_venir_l_administrateur_sans_affectation_voit_tout_le_parc()
    {
        using var parc = await Parc.CreerAsync();

        var fiches = Contenu<List<FicheEntretienDto>>(
            (await Entretiens(parc, AdminSansAffectation()).GetUpcomingMaintenance()).Result);

        fiches.Select(f => f.VehicleId).Should().BeEquivalentTo(new[] { VehiculeA, VehiculeB });
        fiches.Single(f => f.VehicleId == VehiculeB).Vehicle!.Plate.Should().Be("222 TU 2");
    }

    // ───────────────── GET /api/maintenance/{id} ─────────────────

    [Fact]
    public async Task Detail_d_une_fiche_d_un_autre_locataire_rend_le_meme_404_qu_une_fiche_inexistante()
    {
        using var parc = await Parc.CreerAsync();
        var locataire = Entretiens(parc, Locataire());

        (await locataire.GetMaintenanceRecord(FicheB)).Result.Should().BeOfType<NotFoundResult>(
            "IDOR : il suffisait de changer l'identifiant pour lire coûts, prestataire et facture du véhicule d'un autre client");
        (await locataire.GetMaintenanceRecord(Inexistante)).Result.Should().BeOfType<NotFoundResult>(
            "même réponse qu'une fiche inexistante : on ne révèle pas qu'elle existe");

        Contenu<FicheEntretienDto>((await locataire.GetMaintenanceRecord(FicheA)).Result).VehicleId.Should().Be(VehiculeA);
    }

    [Fact]
    public async Task Detail_l_administrateur_sans_affectation_lit_toute_fiche_de_sa_societe()
    {
        using var parc = await Parc.CreerAsync();
        var admin = Entretiens(parc, AdminSansAffectation());

        Contenu<FicheEntretienDto>((await admin.GetMaintenanceRecord(FicheB)).Result).VehicleId.Should().Be(VehiculeB);
        (await admin.GetMaintenanceRecord(FicheAutreSociete)).Result.Should().BeOfType<NotFoundResult>(
            "la portée null lève le filtre véhicule, jamais le filtre société");
    }

    // ───────────────── POST /api/maintenance ─────────────────

    [Fact]
    public async Task Saisie_sur_un_vehicule_hors_portee_refusee_sans_toucher_au_compteur_ni_creer_de_depense()
    {
        using var parc = await Parc.CreerAsync();

        var hors = async () => await Entretiens(parc, Locataire()).CreateMaintenanceRecord(Saisie(VehiculeB, 60_000));
        var refus = await hors.Should().ThrowAsync<NotFoundException>(
            "un entretien « completed » fait avancer le compteur du véhicule et crée une dépense : hors portée, 404");

        // Relu en base de test, par un contexte neuf : RIEN n'a été écrit.
        var etat = await EtatDuVehiculeAsync(parc, VehiculeB);
        etat.km.Should().Be(KmB, "le compteur du véhicule loué à un autre client ne doit pas avoir avancé");
        etat.couts.Should().Be(0, "aucune ligne vehicle_costs ne doit avoir été imputée au véhicule B");
        etat.fiches.Should().Be(1, "seule la fiche d'origine du véhicule B existe");

        // Même réponse qu'un véhicule inexistant : on ne révèle pas qu'il existe.
        var inexistant = async () => await Entretiens(parc, Locataire()).CreateMaintenanceRecord(Saisie(Inexistante, 60_000));
        (await inexistant.Should().ThrowAsync<NotFoundException>()).Which.Message
            .Should().Be(refus.Which.Message);

        // …et le locataire garde la saisie sur SON véhicule : compteur avancé, dépense créée.
        var cree = await Entretiens(parc, Locataire()).CreateMaintenanceRecord(Saisie(VehiculeA, 12_000));
        cree.Result.Should().BeOfType<CreatedAtActionResult>();
        var etatA = await EtatDuVehiculeAsync(parc, VehiculeA);
        etatA.km.Should().Be(12_000);
        etatA.couts.Should().Be(1);
    }

    [Fact]
    public async Task Saisie_l_administrateur_sans_affectation_saisit_sur_tout_le_parc()
    {
        using var parc = await Parc.CreerAsync();

        var cree = await Entretiens(parc, AdminSansAffectation()).CreateMaintenanceRecord(Saisie(VehiculeB, 60_000));

        var reponse = ((CreatedAtActionResult)cree.Result!).Value.Should().BeOfType<FicheEntretienDto>().Subject;
        reponse.VehicleId.Should().Be(VehiculeB);
        reponse.TotalCost.Should().Be(150);

        var etat = await EtatDuVehiculeAsync(parc, VehiculeB);
        etat.km.Should().Be(60_000, "portée null : l'administrateur sans affectation fait avancer tout compteur de sa société");
        etat.couts.Should().Be(1);
        etat.fiches.Should().Be(2);
    }

    [Fact]
    public async Task Saisie_sur_le_vehicule_d_une_autre_societe_refusee_meme_pour_l_administrateur()
    {
        using var parc = await Parc.CreerAsync();

        var autre = async () => await Entretiens(parc, AdminSansAffectation())
            .CreateMaintenanceRecord(Saisie(VehiculeAutreSociete, 60_000));
        await autre.Should().ThrowAsync<NotFoundException>(
            "la portée null lève le filtre véhicule, jamais le filtre société");

        var etat = await EtatDuVehiculeAsync(parc, VehiculeAutreSociete);
        etat.km.Should().Be(1);
        etat.couts.Should().Be(0);
        etat.fiches.Should().Be(1);
    }

    [Fact]
    public async Task Saisie_un_vehicule_joint_au_corps_n_est_jamais_insere()
    {
        using var parc = await Parc.CreerAsync();

        // Sur-affectation : le corps est lié à l'ENTITÉ, navigations comprises.
        var saisie = Saisie(VehiculeA, 12_000);
        saisie.Vehicle = new Vehicle { Name = "Véhicule fantôme", Plate = "000 TU 0", CompanyId = CompanyId, Mileage = 999_999 };

        var cree = await Entretiens(parc, Locataire()).CreateMaintenanceRecord(saisie);

        cree.Result.Should().BeOfType<CreatedAtActionResult>();
        await using var controle = parc.Controle();
        (await controle.Vehicles.IgnoreQueryFilters().CountAsync()).Should().Be(3,
            "Add() insérait tout le graphe reçu : un compte restreint créait un véhicule en passant par l'entretien");
        (await controle.MaintenanceRecords.IgnoreQueryFilters().CountAsync(m => m.VehicleId == VehiculeA)).Should().Be(2,
            "la fiche reste rattachée au véhicule CONTRÔLÉ, pas à celui du graphe");
    }

    [Fact]
    public async Task Saisie_un_non_administrateur_sans_affectation_ne_saisit_rien()
    {
        using var parc = await Parc.CreerAsync();

        var saisie = async () => await Entretiens(parc, NonAdminSansAffectation()).CreateMaintenanceRecord(Saisie(VehiculeA, 12_000));
        await saisie.Should().ThrowAsync<NotFoundException>();

        var etat = await EtatDuVehiculeAsync(parc, VehiculeA);
        etat.km.Should().Be(KmA);
        etat.couts.Should().Be(0);
    }

    // ───────────────── PUT / DELETE / POST {id}/parts ─────────────────

    [Fact]
    public async Task Modifier_la_fiche_d_un_autre_locataire_rend_le_meme_404_qu_une_fiche_inexistante()
    {
        using var parc = await Parc.CreerAsync();
        var corps = new MaintenanceRecord { Type = "repair", Description = "Écrasée", Status = "completed", LaborCost = 1, PartsCost = 1 };

        var hors = async () => await Entretiens(parc, Locataire()).UpdateMaintenanceRecord(FicheB, corps);
        var refus = await hors.Should().ThrowAsync<NotFoundException>();

        var inexistante = async () => await Entretiens(parc, Locataire()).UpdateMaintenanceRecord(Inexistante, corps);
        (await inexistante.Should().ThrowAsync<NotFoundException>()).Which.Message.Should().Be(refus.Which.Message);

        await using (var controle = parc.Controle())
        {
            var ficheB = await controle.MaintenanceRecords.IgnoreQueryFilters().SingleAsync(m => m.Id == FicheB);
            ficheB.Description.Should().Be("Vidange B");
            ficheB.TotalCost.Should().Be(500);
        }

        (await Entretiens(parc, Locataire()).UpdateMaintenanceRecord(FicheA, corps)).Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task Modifier_l_administrateur_sans_affectation_modifie_toute_fiche_de_sa_societe()
    {
        using var parc = await Parc.CreerAsync();
        var corps = new MaintenanceRecord { Type = "repair", Description = "Corrigée", Status = "scheduled", LaborCost = 10, PartsCost = 5, Date = Bientot };

        (await Entretiens(parc, AdminSansAffectation()).UpdateMaintenanceRecord(FicheB, corps)).Should().BeOfType<NoContentResult>();

        await using var controle = parc.Controle();
        (await controle.MaintenanceRecords.IgnoreQueryFilters().SingleAsync(m => m.Id == FicheB)).TotalCost.Should().Be(15);
    }

    [Fact]
    public async Task Supprimer_la_fiche_d_un_autre_locataire_est_refuse()
    {
        using var parc = await Parc.CreerAsync();

        var hors = async () => await Entretiens(parc, Locataire()).DeleteMaintenanceRecord(FicheB);
        await hors.Should().ThrowAsync<NotFoundException>(
            "un locataire ne supprime pas l'historique d'entretien du véhicule loué à un autre client");

        (await EtatDuVehiculeAsync(parc, VehiculeB)).fiches.Should().Be(1);
    }

    [Fact]
    public async Task Supprimer_l_administrateur_sans_affectation_supprime_toute_fiche_de_sa_societe()
    {
        using var parc = await Parc.CreerAsync();

        (await Entretiens(parc, AdminSansAffectation()).DeleteMaintenanceRecord(FicheB)).Should().BeOfType<NoContentResult>();

        (await EtatDuVehiculeAsync(parc, VehiculeB)).fiches.Should().Be(0);
    }

    [Fact]
    public async Task Ajouter_une_piece_a_la_fiche_d_un_autre_locataire_est_refuse()
    {
        using var parc = await Parc.CreerAsync();
        var piece = new MaintenancePart { Name = "Pièce forcée", Quantity = 3, UnitCost = 100 };

        var hors = async () => await Entretiens(parc, Locataire()).AddPart(FicheB, piece);
        await hors.Should().ThrowAsync<NotFoundException>();

        await using var controle = parc.Controle();
        (await controle.MaintenanceParts.CountAsync(p => p.MaintenanceRecordId == FicheB)).Should().Be(1);
        (await controle.MaintenanceRecords.IgnoreQueryFilters().SingleAsync(m => m.Id == FicheB)).PartsCost.Should().Be(200,
            "le coût de la fiche du véhicule loué à un autre client ne doit pas avoir bougé");
    }

    [Fact]
    public async Task Ajouter_une_piece_l_administrateur_sans_affectation_le_peut_sur_toute_fiche()
    {
        using var parc = await Parc.CreerAsync();
        var piece = new MaintenancePart { Name = "Plaquettes", Quantity = 2, UnitCost = 50 };

        (await Entretiens(parc, AdminSansAffectation()).AddPart(FicheB, piece)).Result.Should().BeOfType<OkObjectResult>();

        await using var controle = parc.Controle();
        (await controle.MaintenanceRecords.IgnoreQueryFilters().SingleAsync(m => m.Id == FicheB)).PartsCost.Should().Be(300);
    }
}
