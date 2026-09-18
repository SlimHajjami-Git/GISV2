using FluentAssertions;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Contrôles de saisie d'une réparation (recette GPA du 16/09/2026).
///
///   • DEF-043 : PATCH /api/repairs/{id}/status stockait n'importe quelle chaîne.
///   • DEF-044 : main-d'œuvre et prix unitaire négatifs, quantité nulle acceptés ; le total
///     négatif était déduit du poste « Réparations » des rapports.
///   • DEF-057 : un fournisseur inexistant ou d'une autre société était enregistré.
///
/// Refus attendu : DomainException (400 { message }) et aucune écriture.
/// </summary>
public class RepairInputRulesTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;
    private static readonly DateTime Day = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static TestGisDbContext Seed()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId, Mileage = 10_000 });
        ctx.Suppliers.AddRange(
            new Supplier { Id = 11, Name = "Garage du Centre", Type = "garage", CompanyId = CompanyId },
            new Supplier { Id = 12, Name = "Garage inactif", Type = "garage", CompanyId = CompanyId, IsActive = false },
            new Supplier { Id = 50, Name = "Garage étranger", Type = "garage", CompanyId = OtherCompanyId });
        ctx.SaveChanges();
        return ctx;
    }

    private static CreateRepairPartRequest Part(string name, int quantity, decimal unitPrice) =>
        new(name, null, quantity, unitPrice, null);

    private static CreateRepairCommand Creation(
        decimal laborCost = 50m, int? supplierId = null, params CreateRepairPartRequest[] parts) =>
        new(1, supplierId, "QA réparation", Day, null, laborCost, null, null, parts.ToList(), "autre");

    private static UpdateRepairCommand Modification(
        int id, string status = "completed", decimal laborCost = 50m, int? supplierId = null,
        params CreateRepairPartRequest[] parts) =>
        new(id, 1, supplierId, "QA modification", Day, null, laborCost, status, null, null, parts.ToList(), "autre");

    private static Task<int> Create(TestGisDbContext ctx, CreateRepairCommand command) =>
        new CreateRepairCommandHandler(ctx, Tenant(), Mock.Of<IPublisher>()).Handle(command, CancellationToken.None);

    private static Task<bool> Update(TestGisDbContext ctx, UpdateRepairCommand command) =>
        new UpdateRepairCommandHandler(ctx, Tenant()).Handle(command, CancellationToken.None);

    private static Task<bool> PatchStatus(TestGisDbContext ctx, int id, string status) =>
        new UpdateRepairStatusCommandHandler(ctx, Tenant())
            .Handle(new UpdateRepairStatusCommand(id, status), CancellationToken.None);

    private static Task<Repair> Reload(TestGisDbContext ctx, int id) =>
        ctx.Repairs.AsNoTracking().Include(r => r.Parts).FirstAsync(r => r.Id == id);

    // ── DEF-043 : statut en liste blanche ──────────────────────────────────────

    [Fact]
    public async Task Un_statut_hors_domaine_est_refuse_et_le_statut_reste_inchange()
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation());

        var act = () => PatchStatus(ctx, id, "bidule");

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Contain("« bidule »").And.Contain("pending").And.Contain("cancelled");
        (await Reload(ctx, id)).Status.Should().Be("completed");
    }

    [Theory]
    [InlineData("pending", "pending")]
    [InlineData("in_progress", "in_progress")]
    [InlineData("Cancelled", "cancelled")]
    [InlineData("  COMPLETED ", "completed")]
    public async Task Les_quatre_statuts_du_domaine_passent_sans_casse_et_sont_stockes_normalises(string received, string stored)
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation());

        (await PatchStatus(ctx, id, received)).Should().BeTrue();

        (await Reload(ctx, id)).Status.Should().Be(stored);
    }

    [Fact]
    public async Task Un_statut_vide_est_refuse()
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation());

        await FluentActions.Invoking(() => PatchStatus(ctx, id, "  ")).Should().ThrowAsync<DomainException>();
        (await Reload(ctx, id)).Status.Should().Be("completed");
    }

    [Fact]
    public async Task La_modification_complete_refuse_aussi_un_statut_hors_domaine_sans_rien_ecrire()
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation(laborCost: 50m));

        var act = () => Update(ctx, Modification(id, status: "nimportequoi", laborCost: 80m));

        await act.Should().ThrowAsync<DomainException>();
        var repair = await Reload(ctx, id);
        repair.Status.Should().Be("completed");
        repair.LaborCost.Should().Be(50m, "aucun champ ne doit être modifié en cas de refus");
    }

    [Fact]
    public async Task La_modification_complete_garde_une_reparation_importee_annulee()
    {
        // L'import Excel écrit « cancelled », que la liste déroulante de l'écran ne proposait pas :
        // la modification renvoie la valeur telle quelle et doit passer.
        using var ctx = Seed();
        var id = await Create(ctx, Creation());
        await PatchStatus(ctx, id, "cancelled");

        (await Update(ctx, Modification(id, status: "Cancelled"))).Should().BeTrue();

        (await Reload(ctx, id)).Status.Should().Be("cancelled");
    }

    // ── DEF-044 : montants et quantités ────────────────────────────────────────

    [Fact]
    public async Task Une_main_d_oeuvre_negative_est_refusee_a_la_creation()
    {
        using var ctx = Seed();

        var act = () => Create(ctx, Creation(laborCost: -50m));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().StartWith("Main-d'œuvre");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Une_quantite_nulle_est_refusee_en_nommant_la_ligne_de_piece()
    {
        using var ctx = Seed();

        var act = () => Create(ctx, Creation(50m, null, Part("Filtre à huile", 1, 12m), Part("Plaquettes", 0, 99m)));

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("Pièce n° 2 (« Plaquettes ») : la quantité doit être un nombre entier supérieur à zéro.");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
        (await ctx.RepairParts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Une_quantite_negative_est_refusee()
    {
        using var ctx = Seed();

        await FluentActions.Invoking(() => Create(ctx, Creation(50m, null, Part("Ampoule", -2, 5m))))
            .Should().ThrowAsync<DomainException>().WithMessage("*quantité*");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_prix_unitaire_negatif_est_refuse()
    {
        using var ctx = Seed();

        var act = () => Create(ctx, Creation(10m, null, Part("Remise", 1, -40m)));

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Message.Should().Be("Pièce n° 1 (« Remise ») : le prix unitaire ne peut pas être négatif.");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_montant_au_dela_de_la_colonne_est_refuse_au_lieu_d_une_erreur_500()
    {
        using var ctx = Seed();

        await FluentActions.Invoking(() => Create(ctx, Creation(laborCost: 100_000_000m)))
            .Should().ThrowAsync<DomainException>().WithMessage("Main-d'œuvre*dépasser*");
    }

    [Fact]
    public async Task Les_montants_nuls_restent_permis()
    {
        // Pièce offerte, main-d'œuvre sous garantie : zéro est une saisie légitime.
        using var ctx = Seed();

        var id = await Create(ctx, Creation(0m, null, Part("Pièce sous garantie", 1, 0m)));

        var repair = await Reload(ctx, id);
        repair.TotalCost.Should().Be(0m);
        repair.Parts.Should().ContainSingle();
    }

    [Fact]
    public async Task La_modification_refuse_un_montant_negatif_et_garde_les_pieces()
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation(50m, null, Part("Filtre", 2, 10m)));

        var act = () => Update(ctx, Modification(id, "completed", 50m, null, Part("Filtre", 2, -10m)));

        await act.Should().ThrowAsync<DomainException>().WithMessage("Pièce n° 1*prix unitaire*");
        var repair = await Reload(ctx, id);
        repair.TotalCost.Should().Be(70m);
        repair.Parts.Should().ContainSingle().Which.UnitPrice.Should().Be(10m);
    }

    // ── DEF-057 : fournisseur de la société ────────────────────────────────────

    [Fact]
    public async Task Un_fournisseur_inexistant_est_refuse_a_la_creation()
    {
        using var ctx = Seed();

        var act = () => Create(ctx, Creation(supplierId: 999));

        (await act.Should().ThrowAsync<DomainException>()).Which.Message.Should().StartWith("Fournisseur introuvable");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Le_fournisseur_d_une_autre_societe_est_refuse_a_la_creation()
    {
        using var ctx = Seed();

        await FluentActions.Invoking(() => Create(ctx, Creation(supplierId: 50)))
            .Should().ThrowAsync<DomainException>();
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(11)]
    [InlineData(12)] // désactivé mais toujours de la société : les réparations passées le référencent
    public async Task Sans_fournisseur_ou_avec_un_fournisseur_de_la_societe_la_creation_passe(int? supplierId)
    {
        using var ctx = Seed();

        var id = await Create(ctx, Creation(supplierId: supplierId));

        (await Reload(ctx, id)).SupplierId.Should().Be(supplierId);
    }

    [Fact]
    public async Task La_modification_refuse_de_passer_a_un_fournisseur_d_une_autre_societe()
    {
        using var ctx = Seed();
        var id = await Create(ctx, Creation(supplierId: 11));

        var act = () => Update(ctx, Modification(id, supplierId: 50));

        await act.Should().ThrowAsync<DomainException>();
        (await Reload(ctx, id)).SupplierId.Should().Be(11);
    }

    [Fact]
    public async Task La_modification_garde_un_fournisseur_supprime_depuis_s_il_n_est_pas_change()
    {
        // Un fournisseur se supprime physiquement, sans clé étrangère : l'écran renvoie la
        // valeur déjà enregistrée, qui ne doit pas bloquer la modification d'une réparation ancienne.
        using var ctx = Seed();
        ctx.Repairs.Add(new Repair
        {
            Id = 300, SocieteId = CompanyId, VehicleId = 1, SupplierId = 77, Reference = "REP-202601-0001",
            RepairDate = Day, LaborCost = 20m, TotalCost = 20m, Status = "completed"
        });
        await ctx.SaveChangesAsync();

        (await Update(ctx, Modification(300, laborCost: 25m, supplierId: 77))).Should().BeTrue();

        var repair = await Reload(ctx, 300);
        repair.SupplierId.Should().Be(77);
        repair.LaborCost.Should().Be(25m);
    }
}
