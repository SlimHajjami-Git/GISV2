using FluentAssertions;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Repairs.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Portée véhicules des réparations (écran Dépenses, onglet Réparations).
///
/// Constat du 09/09/2026 : les trois requêtes ne filtraient que sur la société,
/// si bien qu'un employé restreint à UN véhicule voyait les 9 réparations de tout
/// le parc — matricules compris — alors que ses pleins, ses échéances et les
/// rapports, eux, étaient bien filtrés. C'était à la fois une incohérence
/// d'affichage et une fuite.
///
/// Sémantique attendue (celle de <c>VehicleScope</c>, comme
/// <c>GetFuelEntriesQueryHandler</c>) :
///   • admin de société            → tout le parc ;
///   • employé avec affectations   → ses véhicules seulement ;
///   • employé SANS affectation    → rien du tout (jamais « tout » par défaut).
/// </summary>
public class RepairVehicleScopeTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int RestrictedUserId = 51;
    private const int OrphanUserId = 52;

    private static readonly DateTime RepairDay = new(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Admin() => Tenant(1, "company_admin");
    private static ICurrentTenantService Restricted() => Tenant(RestrictedUserId, "user");
    private static ICurrentTenantService Orphan() => Tenant(OrphanUserId, "user");

    /// <summary>
    /// Société 1 : véhicules 1 (affecté à l'employé) et 2, 3 (non affectés).
    /// Une réparation par véhicule + une réparation d'une autre société.
    /// </summary>
    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Camion", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 3, Name = "Fourgon", Plate = "333 TU 3", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId });

        ctx.Repairs.AddRange(
            new Repair { Id = 101, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", Description = "Plaquettes", RepairDate = RepairDay, TotalCost = 100, LaborCost = 40, PartsCost = 60, Status = "completed" },
            new Repair { Id = 102, SocieteId = CompanyId, VehicleId = 2, Reference = "REP-2", Description = "Embrayage", RepairDate = RepairDay, TotalCost = 500, LaborCost = 200, PartsCost = 300, Status = "pending" },
            new Repair { Id = 103, SocieteId = CompanyId, VehicleId = 3, Reference = "REP-3", Description = "Vidange", RepairDate = RepairDay, TotalCost = 80, LaborCost = 30, PartsCost = 50, Status = "completed" },
            new Repair { Id = 104, SocieteId = OtherCompanyId, VehicleId = 9, Reference = "REP-4", Description = "Pneu", RepairDate = RepairDay, TotalCost = 90, Status = "completed" });

        // Seul le véhicule 1 est affecté à l'employé restreint.
        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
    }

    private static async Task<List<int>> ListedIdsAsync(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var result = await new GetRepairsQueryHandler(ctx, tenant)
            .Handle(new GetRepairsQuery(), CancellationToken.None);
        result.TotalCount.Should().Be(result.Items.Count, "le compteur de pagination porte sur la même requête que la liste");
        return result.Items.Select(r => r.Id).OrderBy(i => i).ToList();
    }

    [Fact]
    public async Task Company_admin_sees_every_repair_of_the_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Admin()))
            .Should().Equal(new[] { 101, 102, 103 }, "un admin voit tout le parc, jamais restreint par les affectations");
    }

    [Fact]
    public async Task Restricted_user_only_sees_repairs_of_the_vehicles_assigned_to_them()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Restricted()))
            .Should().Equal(new[] { 101 }, "seul le véhicule 1 lui est affecté");
    }

    [Fact]
    public async Task User_without_any_assignment_sees_nothing()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Orphan()))
            .Should().BeEmpty("liste d'affectations vide = aucun véhicule visible, surtout pas tout le parc");
    }

    [Fact]
    public async Task Stats_are_computed_on_the_same_scope_as_the_list()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var query = new GetRepairStatsQuery();

        var admin = await new GetRepairStatsQueryHandler(ctx, Admin()).Handle(query, CancellationToken.None);
        admin.TotalRepairs.Should().Be(3);
        admin.TotalCost.Should().Be(680m);

        var restricted = await new GetRepairStatsQueryHandler(ctx, Restricted()).Handle(query, CancellationToken.None);
        restricted.TotalRepairs.Should().Be(1, "le KPI doit porter sur les mêmes lignes que la liste affichée");
        restricted.TotalCost.Should().Be(100m);
        restricted.AverageCost.Should().Be(100m);

        var orphan = await new GetRepairStatsQueryHandler(ctx, Orphan()).Handle(query, CancellationToken.None);
        orphan.TotalRepairs.Should().Be(0);
        orphan.TotalCost.Should().Be(0m);
    }

    [Fact]
    public async Task Detail_by_id_of_a_non_assigned_vehicle_is_not_found_for_a_restricted_user()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // 102 appartient au véhicule 2, non affecté : invisible même en le demandant par son id.
        (await new GetRepairByIdQueryHandler(ctx, Restricted())
            .Handle(new GetRepairByIdQuery(102), CancellationToken.None))
            .Should().BeNull();

        // Sa propre réparation reste accessible.
        var mine = await new GetRepairByIdQueryHandler(ctx, Restricted())
            .Handle(new GetRepairByIdQuery(101), CancellationToken.None);
        mine.Should().NotBeNull();
        mine!.VehiclePlate.Should().Be("111 TU 1");

        // L'admin, lui, ouvre les deux.
        (await new GetRepairByIdQueryHandler(ctx, Admin())
            .Handle(new GetRepairByIdQuery(102), CancellationToken.None))
            .Should().NotBeNull();
    }
}
