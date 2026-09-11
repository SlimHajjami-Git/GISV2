using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Application.Features.Users.Commands.UpdateUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// Une case par rapport (recette client du 11/09/2026, migration 046) : les quatre
/// rapports de coûts, « Consommation carburant mensuel », les deux rapports carburant GPS
/// et le Rapport IA Flotte ont chacun leur permission, du chemin HTTP (PermissionMiddleware)
/// au jeton de connexion et à l'éditeur d'utilisateurs. Avant, cinq d'entre eux héritaient
/// d'une case partagée et trois n'en avaient aucune.
/// </summary>
public class ReportPermissionsTests
{
    /// <summary>Les huit cases nées de la migration 046 — un champ par nom, sur User et sur le DTO.</summary>
    public static readonly string[] NewFlags =
    {
        "CanReportOperatingCost", "CanReportCostEvolution", "CanReportCostRanking", "CanReportRepairFrequency",
        "CanReportMonthlyFuel", "CanReportAiFleet", "CanReportFuelEstimation", "CanReportFuelComparison",
    };

    public static IEnumerable<object[]> EachNewFlag() => NewFlags.Select(f => new object[] { f });

    private static bool Flag(object target, string name) =>
        (bool)target.GetType().GetProperty(name)!.GetValue(target)!;

    // ── Chemin HTTP → permission exigée (préfixe le plus long) ──────────────────

    [Theory]
    [InlineData("/api/reports/costs", "CanReportCosts")]
    [InlineData("/api/reports/costs/operating", "CanReportOperatingCost")]
    [InlineData("/api/reports/costs/evolution/12", "CanReportCostEvolution")]
    [InlineData("/api/reports/costs/ranking", "CanReportCostRanking")]
    [InlineData("/api/reports/costs/repair-frequency", "CanReportRepairFrequency")]
    [InlineData("/api/reports/monthly-costs", "CanReportMonthlyCosts")]
    [InlineData("/api/reports/monthly-fuel", "CanReportMonthlyFuel")]
    [InlineData("/api/reports/monthly", "CanReportMonthly")]
    [InlineData("/api/ai-chat/fleet-report", "CanReportAiFleet")]
    [InlineData("/api/ai-chat/fleet-report/ask", "CanReportAiFleet")]
    [InlineData("/api/ai-chat/send", null)]                               // l'assistant reste libre
    [InlineData("/api/fuelexpenses/statistics", "CanReportFuelEstimation")]
    [InlineData("/api/fuelexpenses/comparison", "CanReportFuelComparison")]
    [InlineData("/api/fuelexpenses/vehicle-audit", "CanReportFuelComparison")]
    [InlineData("/api/fuelexpenses/real-consumption", "CanFuel")]         // saisie manuelle : module Carburant
    [InlineData("/api/fuelexpenses", "CanFuel")]
    [InlineData("/api/reports", "CanReports")]
    public void Chaque_rapport_exige_sa_propre_permission(string path, string? expected)
        => PermissionMiddleware.RequiredUserPermission(path).Should().Be(expected);

    [Theory]
    [MemberData(nameof(EachNewFlag))]
    public void Une_case_decochee_ferme_son_rapport_et_lui_seul(string flag)
    {
        var user = new User { CanReports = true };
        typeof(User).GetProperty(flag)!.SetValue(user, false);

        PermissionMiddleware.IsGranted(user, flag).Should().BeFalse();
        foreach (var other in NewFlags.Where(f => f != flag))
            PermissionMiddleware.IsGranted(user, other).Should().BeTrue($"{other} ne dépend pas de {flag}");
        PermissionMiddleware.IsGranted(user, "CanReportCosts").Should().BeTrue("« Réparations véhicules » ne porte plus les rapports de coûts");
        PermissionMiddleware.IsGranted(user, "CanReportMonthlyCosts").Should().BeTrue();
    }

    [Fact]
    public void Sans_le_module_Rapports_aucune_case_n_ouvre_rien()
    {
        var user = new User { CanReports = false };

        foreach (var flag in NewFlags)
            PermissionMiddleware.IsGranted(user, flag).Should().BeFalse(flag);
    }

    // ── Jeton de connexion : chaque case voyage jusqu'au navigateur, à sa place ──

    [Theory]
    [MemberData(nameof(EachNewFlag))]
    public void Le_jeton_de_connexion_porte_chaque_case_a_sa_place(string flag)
    {
        var user = TestDataBuilder.CreateUser();
        typeof(User).GetProperty(flag)!.SetValue(user, false);

        var dto = LoginCommandHandler.BuildUserPermissions(user);

        foreach (var f in NewFlags)
            Flag(dto, f).Should().Be(f != flag, $"{f} (seule {flag} est décochée)");
    }

    // ── Éditeur d'utilisateurs : enregistrement case par case, puis relecture ────

    [Theory]
    [MemberData(nameof(EachNewFlag))]
    public async Task L_editeur_enregistre_chaque_case_separement(string flag)
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: 14));
        ctx.Users.Add(TestDataBuilder.CreateUser(id: 51, companyId: 14, email: "employe@test.com"));
        await ctx.SaveChangesAsync();

        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(14);
        tenant.Setup(x => x.UserId).Returns(45);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);

        // Une seule case décochée, les autres absentes de la commande (null = inchangé).
        var command = new UpdateUserCommand(
            Id: 51, FirstName: "Em", LastName: "Ployé", Email: "employe@test.com", Phone: null, RoleId: 1, Status: "active");
        typeof(UpdateUserCommand).GetProperty(flag)!.SetValue(command, false);

        await new UpdateUserCommandHandler(ctx, tenant.Object).Handle(command, CancellationToken.None);

        // Relecture par l'entité (GetCurrentUser fait un Include(UserVehicles) que le
        // contexte InMemory des tests ne sait pas traduire).
        ctx.ChangeTracker.Clear();
        var saved = (await ctx.Users.FindAsync(51))!;
        foreach (var f in NewFlags)
            Flag(saved, f).Should().Be(f != flag, $"{f} (seule {flag} a été décochée)");
        saved.CanReportCosts.Should().BeTrue();
        saved.CanReportMonthlyCosts.Should().BeTrue();
    }

    // ── monthly-fuel : la réponse ne porte que le carburant ─────────────────────

    [Fact]
    public void La_route_monthly_fuel_ne_livre_que_le_carburant()
    {
        var vehicle = new VehicleMonthlyCostDto
        {
            VehicleId = 368, Km = 1000, FuelCostDzd = 200, FuelLiters = 80, FuelLitersPr = 70, KmPr = 900,
            MaintenanceCostDzd = 300, RepairCostDzd = 400, OtherCostDzd = 50, TotalCostDzd = 950,
            CostPerKm = 0.95m, FuelPer100Km = 20, MaintenanceRepairPer100Km = 70, ConsumptionPer100Km = 8,
        };
        var dto = new MonthlyCostReportDto
        {
            TotalKm = 1000, TotalFuelCostDzd = 200, TotalFuelLiters = 80,
            TotalMaintenanceCostDzd = 300, TotalRepairCostDzd = 400, TotalOtherCostDzd = 50, TotalCostDzd = 950,
            Departments = { new DepartmentCostGroupDto { TotalFuelCostDzd = 200, TotalMaintenanceCostDzd = 300, TotalRepairCostDzd = 400, TotalOtherCostDzd = 50, TotalCostDzd = 950, Vehicles = { vehicle } } },
            Vehicles = { vehicle },
        };

        var fuel = dto.ToFuelOnly().ToFuelOnly();   // idempotent

        fuel.TotalMaintenanceCostDzd.Should().Be(0);
        fuel.TotalRepairCostDzd.Should().Be(0);
        fuel.TotalOtherCostDzd.Should().Be(0);
        fuel.TotalCostDzd.Should().Be(200, "le total devient le carburant seul");
        fuel.Departments.Single().TotalCostDzd.Should().Be(200);
        fuel.Departments.Single().TotalMaintenanceCostDzd.Should().Be(0);
        var v = fuel.Vehicles.Single();
        v.MaintenanceCostDzd.Should().Be(0);
        v.RepairCostDzd.Should().Be(0);
        v.OtherCostDzd.Should().Be(0);
        v.TotalCostDzd.Should().Be(200);
        v.CostPerKm.Should().Be(0.2m);
        v.MaintenanceRepairPer100Km.Should().Be(0);
        // Ce que l'écran carburant lit reste intact.
        v.FuelCostDzd.Should().Be(200);
        v.FuelLiters.Should().Be(80);
        v.FuelLitersPr.Should().Be(70);
        v.KmPr.Should().Be(900);
        v.ConsumptionPer100Km.Should().Be(8);
        v.FuelPer100Km.Should().Be(20);
        fuel.TotalFuelLiters.Should().Be(80);
        fuel.TotalKm.Should().Be(1000);
    }
}
