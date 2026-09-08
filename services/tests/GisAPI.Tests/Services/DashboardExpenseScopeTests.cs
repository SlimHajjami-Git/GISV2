using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Services;

/// <summary>
/// Tests des dépenses du tableau de bord — ils appellent les MÉTHODES DE
/// PRODUCTION <c>GisAPI.Services.DashboardService.ScopeIdsAsync</c> et
/// <c>PeriodCostsAsync</c> contre le contexte SQLite en mémoire.
///
/// Avant le 09/09/2026 ce fichier recopiait ces deux méthodes : les tests
/// restaient VERTS même si le filtre de portée disparaissait de la production.
/// Le projet de tests référence désormais <c>GisAPI.csproj</c> et la portée y est
/// devenue une méthode statique publique, testable telle quelle — il n'existe
/// plus qu'UNE définition, et retirer le filtre de portée du service fait
/// échouer ces tests.
///
/// Trois constats couverts :
///   • la portée véhicules (<c>scopeIds</c>) n'était appliquée qu'au poste
///     « acquisition » : les quatre autres (carburant, entretien, réparations,
///     autres) sommaient TOUT le parc, si bien que le « Coût total » d'un employé
///     restreint contredisait à la fois la liste de ses véhicules et les rapports ;
///     et une liste d'affectations VIDE valait « voit tout » au lieu de « ne voit rien » ;
///   • un plein SANS véhicule était compté par le tableau de bord d'un admin,
///     jamais par les rapports (OperatingCostAggregator exige un véhicule) : le
///     même plein entrait dans un écran et pas dans l'autre ;
///   • la période précédente ajoutait les MaintenanceLogs AUX VehicleCosts, alors
///     que la période courante ne compte que les VehicleCosts « maintenance » :
///     chaque entretien du passé était compté deux fois (l'écran crée une dépense
///     ET un journal qui la référence), ce qui faussait la flèche de tendance.
/// </summary>
public class DashboardExpenseScopeTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int RestrictedUserId = 51;
    private const int OrphanUserId = 52;

    private static readonly DateTime PeriodStart = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime PrevStart = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PrevEnd = new(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc);

    private static DateTime June(int day) => new(2026, 6, day, 8, 0, 0, DateTimeKind.Utc);
    private static DateTime May(int day) => new(2026, 5, day, 8, 0, 0, DateTimeKind.Utc);

    // ── Les appels de production, sans copie ────────────────────────────────────
    private static Task<List<int>?> ScopeIdsAsync(TestGisDbContext ctx, bool isAdmin, int userId) =>
        DashboardService.ScopeIdsAsync(ctx, isAdmin, userId, CancellationToken.None);

    private static Task<(decimal Fuel, decimal Maintenance, decimal Repair, decimal Other)> PeriodCostsAsync(
        TestGisDbContext ctx, int companyId, List<int>? scopeIds, DateTime from, DateTime to) =>
        DashboardService.PeriodCostsAsync(ctx, companyId, scopeIds, from, to, CancellationToken.None);

    private static async Task<decimal> TotalAsync(TestGisDbContext ctx, bool isAdmin, int userId, DateTime from, DateTime to)
    {
        var scope = await ScopeIdsAsync(ctx, isAdmin, userId);
        var (fuel, maintenance, repair, other) = await PeriodCostsAsync(ctx, CompanyId, scope, from, to);
        return fuel + maintenance + repair + other;
    }

    /// <summary>
    /// Société 1 : véhicule 1 affecté à l'employé 51, véhicule 2 non affecté.
    /// Juin (période courante) et mai (période précédente, avec un entretien qui
    /// porte à la fois une dépense et son journal).
    /// </summary>
    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Camion", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId });

        ctx.FuelEntries.AddRange(
            new FuelEntry { Id = 1, CompanyId = CompanyId, VehicleId = 1, InvoiceDate = June(3), Volume = 60, TotalAmount = 100 },
            new FuelEntry { Id = 2, CompanyId = CompanyId, VehicleId = 2, InvoiceDate = June(4), Volume = 120, TotalAmount = 200 },
            // Plein sans véhicule : ne compte NULLE PART (ni pour un employé, ni
            // pour un admin), comme dans les rapports de coûts.
            new FuelEntry { Id = 3, CompanyId = CompanyId, VehicleId = null, InvoiceDate = June(5), Volume = 30, TotalAmount = 50 },
            new FuelEntry { Id = 4, CompanyId = OtherCompanyId, VehicleId = 9, InvoiceDate = June(6), Volume = 10, TotalAmount = 999 });

        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "fuel", Amount = 60, Date = June(7) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "maintenance", Amount = 120, Date = June(8) },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 1, Type = "insurance", Amount = 830, Date = June(9) },
            new VehicleCost { Id = 4, CompanyId = CompanyId, VehicleId = 2, Type = "maintenance", Amount = 50, Date = June(10) },
            new VehicleCost { Id = 5, CompanyId = CompanyId, VehicleId = 2, Type = "tax", Amount = 20, Date = June(11) },
            // Période précédente : l'entretien de mai, saisi depuis l'écran → une
            // dépense ET un journal qui la référence.
            new VehicleCost { Id = 6, CompanyId = CompanyId, VehicleId = 1, Type = "maintenance", Amount = 200, Date = May(12) });

        ctx.MaintenanceLogs.Add(new MaintenanceLog
        {
            Id = 1,
            VehicleId = 1,
            CompanyId = CompanyId,
            CostId = 6,
            DoneDate = May(12),
            ActualCost = 200
        });

        ctx.Repairs.AddRange(
            new Repair { Id = 101, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", RepairDate = June(12), TotalCost = 100, Status = "completed" },
            new Repair { Id = 102, SocieteId = CompanyId, VehicleId = 2, Reference = "REP-2", RepairDate = June(13), TotalCost = 500, Status = "completed" },
            // Annulée : ne doit compter nulle part — les rapports de coûts
            // l'excluent déjà, le tableau de bord l'additionnait encore.
            new Repair { Id = 103, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-3", RepairDate = June(14), TotalCost = 999, Status = "Cancelled" });

        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Company_admin_totals_cover_the_whole_fleet()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: 1);
        scope.Should().BeNull("un admin n'est jamais restreint par les affectations");

        var (fuel, maintenance, repair, other) = await PeriodCostsAsync(ctx, CompanyId, scope, PeriodStart, PeriodEnd);
        fuel.Should().Be(360m, "100 + 200 de pleins rattachés à un véhicule + 60 de dépense carburant ; le plein sans véhicule ne compte pas");
        maintenance.Should().Be(170m);
        repair.Should().Be(600m, "la réparation annulée de 999 est exclue, comme dans les rapports de coûts");
        other.Should().Be(850m, "assurance 830 + vignette 20");
        (fuel + maintenance + repair + other).Should().Be(1_980m);
    }

    [Fact]
    public async Task Une_reparation_annulee_ne_compte_dans_aucun_total()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // Même règle que OperatingCostAggregator : sans elle, le tableau de bord
        // et le rapport de coûts affichaient deux totaux différents pour le même mois.
        var (_, _, repairAdmin, _) = await PeriodCostsAsync(ctx, CompanyId, null, PeriodStart, PeriodEnd);
        repairAdmin.Should().Be(600m).And.NotBe(1_599m);

        var restricted = await ScopeIdsAsync(ctx, isAdmin: false, userId: RestrictedUserId);
        var (_, _, repairRestricted, _) = await PeriodCostsAsync(ctx, CompanyId, restricted, PeriodStart, PeriodEnd);
        repairRestricted.Should().Be(100m, "seule la réparation terminée du véhicule 1 est visible et comptée");
    }

    [Fact]
    public async Task Fuel_entry_without_a_vehicle_is_counted_nowhere()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var orphan = await ctx.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == CompanyId && f.VehicleId == null
                        && f.InvoiceDate >= PeriodStart && f.InvoiceDate <= PeriodEnd)
            .Select(f => (decimal?)f.TotalAmount).SumAsync() ?? 0m;
        orphan.Should().Be(50m, "le plein sans véhicule existe bien en base : la saisie l'autorise");

        var adminTotal = await TotalAsync(ctx, isAdmin: true, userId: 1, PeriodStart, PeriodEnd);
        var restrictedTotal = await TotalAsync(ctx, isAdmin: false, userId: RestrictedUserId, PeriodStart, PeriodEnd);

        adminTotal.Should().Be(1_980m,
            "le rattachement se fait par véhicule, comme OperatingCostAggregator : sans cela le même " +
            "plein entrait dans le tableau de bord et jamais dans le rapport mensuel");
        restrictedTotal.Should().Be(1_210m, "un plein sans véhicule n'appartient à la portée de personne");
    }

    [Fact]
    public async Task Restricted_user_totals_only_cover_their_assigned_vehicles()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: RestrictedUserId);
        scope.Should().Equal(new[] { 1 });

        var (fuel, maintenance, repair, other) = await PeriodCostsAsync(ctx, CompanyId, scope, PeriodStart, PeriodEnd);
        fuel.Should().Be(160m, "son plein 100 + sa dépense carburant 60");
        maintenance.Should().Be(120m);
        repair.Should().Be(100m);
        other.Should().Be(830m);
        (fuel + maintenance + repair + other).Should().Be(1_210m,
            "le total du tableau de bord porte sur les mêmes véhicules que la liste et que les rapports");
    }

    [Fact]
    public async Task User_without_any_assignment_sees_zero_everywhere()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: OrphanUserId);
        scope.Should().NotBeNull().And.BeEmpty("liste vide = aucun véhicule visible, surtout pas tout le parc");

        var (fuel, maintenance, repair, other) = await PeriodCostsAsync(ctx, CompanyId, scope, PeriodStart, PeriodEnd);
        (fuel + maintenance + repair + other).Should().Be(0m);
    }

    [Fact]
    public async Task Unidentified_caller_sees_nothing()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // Même règle que VehicleScope : sans identifiant et sans drapeau admin,
        // la portée est VIDE (fail-closed) — pas « tout le parc ».
        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: 0);
        scope.Should().NotBeNull().And.BeEmpty();

        (await TotalAsync(ctx, isAdmin: false, userId: 0, PeriodStart, PeriodEnd)).Should().Be(0m);
    }

    [Fact]
    public async Task Previous_period_uses_the_same_definition_and_never_counts_maintenance_twice()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        // Mai ne contient qu'un entretien de 200 — présent DEUX fois en base
        // (VehicleCost #6 + MaintenanceLog #1 qui la référence).
        var logged = await ctx.MaintenanceLogs.AsNoTracking()
            .Where(m => m.DoneDate >= PrevStart && m.DoneDate <= PrevEnd && m.ActualCost > 0)
            .Select(m => (decimal?)m.ActualCost).SumAsync() ?? 0m;
        logged.Should().Be(200m, "le journal existe bien : c'est ce doublon que l'ancien calcul ajoutait");

        (await TotalAsync(ctx, isAdmin: true, userId: 1, PrevStart, PrevEnd))
            .Should().Be(200m, "l'entretien n'est compté qu'une fois, par sa dépense");

        (await TotalAsync(ctx, isAdmin: false, userId: RestrictedUserId, PrevStart, PrevEnd))
            .Should().Be(200m, "le véhicule 1 lui est affecté");

        (await TotalAsync(ctx, isAdmin: false, userId: OrphanUserId, PrevStart, PrevEnd))
            .Should().Be(0m, "la période précédente est bornée par la même portée que la période courante");
    }
}
