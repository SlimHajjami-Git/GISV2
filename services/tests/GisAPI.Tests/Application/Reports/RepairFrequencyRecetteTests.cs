using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetRepairFrequencyReport;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Recette du 11/09/2026 sur « Fréquence des réparations » : ses coûts sont
/// ceux des interventions qu'il compte, pas ceux des dépenses « Réparation »
/// que l'agrégateur range désormais en Réparations pour les rapports de coûts.
/// </summary>
public class RepairFrequencyRecetteTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Debut = new(2026, 7, 1);
    private static readonly DateTime Fin = new(2026, 8, 31);

    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Logistique", Plate = "GH-619-XC", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Sinistre", Plate = "GG-852-BD", CompanyId = CompanyId });
        ctx.Repairs.AddRange(
            new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", RepairType = "mecanique", RepairDate = Utc(7, 3), TotalCost = 100, Status = "completed" },
            new Repair { Id = 2, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-2", RepairType = "electrique", RepairDate = Utc(8, 3), TotalCost = 300, Status = "completed" });
        ctx.VehicleCosts.AddRange(
            // Facture de sinistre (phase 5) sur le véhicule réparé...
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 2_000, Date = Utc(8, 10) },
            // ...et sur un véhicule qui n'a aucune intervention à l'atelier.
            new VehicleCost { VehicleId = 2, CompanyId = CompanyId, Type = "repair", Amount = 900, Date = Utc(8, 12) });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Une_depense_reparation_ne_gonfle_pas_le_cout_des_interventions()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetRepairFrequencyReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetRepairFrequencyReportQuery(Debut, Fin), CancellationToken.None);

        rapport.TotalInterventions.Should().Be(2);
        rapport.TotalRepairCost.Should().Be(400m, "100 + 300 des deux interventions, sans les 2 900 de dépenses");
        rapport.AverageCostPerIntervention.Should().Be(200m, "et non (400 + 2 900) / 2");
        rapport.ByType.Sum(t => t.TotalCost).Should().Be(rapport.TotalRepairCost, "la somme des types recoupe le total");

        var logistique = rapport.Vehicles.Single(v => v.VehicleId == 1);
        logistique.TotalCost.Should().Be(400m);
        logistique.AverageCostPerIntervention.Should().Be(200m);
        rapport.MostFrequentVehicleInterventions.Sum(i => i.TotalCost).Should().Be(logistique.TotalCost,
            "le détail recoupe le coût du véhicule");

        var sinistre = rapport.Vehicles.Single(v => v.VehicleId == 2);
        sinistre.Interventions.Should().Be(0);
        sinistre.TotalCost.Should().Be(0m, "aucune intervention : pas de coût affiché dans un rapport d'interventions");
    }

    [Fact]
    public async Task Le_cout_d_exploitation_garde_la_depense_reparation_dans_sa_colonne()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetOperatingCostReportQuery(Debut, Fin), CancellationToken.None);

        rapport.Vehicles.Single(v => v.VehicleId == 1).RepairCost.Should().Be(2_400m,
            "le rapport de coûts compte tout ce que coûtent les réparations, factures de sinistre comprises");
    }
}
