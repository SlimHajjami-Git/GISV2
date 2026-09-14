using FluentAssertions;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Services;

/// <summary>
/// Une dépense se ventile de la MÊME façon dans les trois calculs de coûts :
/// tableau de bord GPS (<c>DashboardService.PeriodCostsAsync</c>), agrégateur des
/// rapports de coûts et du tableau de bord GPA, et « Coûts mensuels par véhicule ».
///
/// Constat du 14/09/2026 : l'agrégateur rangeait « repair » en Réparations et
/// soustrayait « insurance_refund », les deux autres les comptaient en « Autres »,
/// en positif. Société 1, avril 2026 : la dépense « repair » de 1 143 400 était en
/// Réparations dans un écran et en Autres dans le voisin ; un remboursement R
/// écartait les totaux de 2R.
/// </summary>
public class CostCategoryParityTests
{
    private const int CompanyId = 1;
    private static DateTime Sept(int day) => new(2026, 9, day, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("fuel", CostCategory.Fuel, 1)]
    [InlineData("maintenance", CostCategory.Maintenance, 1)]
    [InlineData(" Entretien ", CostCategory.Maintenance, 1)]
    [InlineData("repair", CostCategory.Repair, 1)]
    [InlineData("Réparation", CostCategory.Repair, 1)]
    [InlineData("reparation", CostCategory.Repair, 1)]
    [InlineData("insurance_refund", CostCategory.Other, -1)]
    [InlineData("insurance", CostCategory.Other, 1)]
    [InlineData(null, CostCategory.Other, 1)]
    public void La_ventilation_d_une_depense_est_unique(string? type, CostCategory expected, int sign) =>
        VehicleCostCategory.Classify(type).Should().Be((expected, sign));

    [Fact]
    public async Task Tableau_de_bord_GPS_agregateur_et_couts_mensuels_donnent_les_memes_postes()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.FuelEntries.Add(new FuelEntry { Id = 1, CompanyId = CompanyId, VehicleId = 1, InvoiceDate = Sept(2), Volume = 60, TotalAmount = 100 });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "fuel", Amount = 60, Date = Sept(3) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "Entretien ", Amount = 120, Date = Sept(4) },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 1, Type = "maintenance", Amount = 50, Date = Sept(5) },
            new VehicleCost { Id = 4, CompanyId = CompanyId, VehicleId = 1, Type = "repair", Amount = 300, Date = Sept(6) },
            new VehicleCost { Id = 5, CompanyId = CompanyId, VehicleId = 1, Type = "insurance", Amount = 600, Date = Sept(7) },
            new VehicleCost { Id = 6, CompanyId = CompanyId, VehicleId = 1, Type = "insurance_refund", Amount = 250, Date = Sept(8) },
            new VehicleCost { Id = 7, CompanyId = CompanyId, VehicleId = 1, Type = "tax", Amount = 20, Date = Sept(9) });
        ctx.Repairs.AddRange(
            new Repair { Id = 1, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", RepairDate = Sept(10), TotalCost = 100, Status = "completed" },
            new Repair { Id = 2, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-2", RepairDate = Sept(11), TotalCost = 999, Status = "Cancelled" });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

        // 1. Tableau de bord GPS (période du mois, bornes incluses).
        var (fuel, maintenance, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc), CancellationToken.None);

        // 2. Agrégateur (rapports de coûts, tableau de bord GPA).
        var aggregate = await OperatingCostAggregator.LoadAsync(ctx, tenant,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None);
        var total = aggregate.Vehicles.Single().Total;

        // 3. Coûts mensuels par véhicule.
        var monthly = await new GetMonthlyCostReportQueryHandler(ctx, tenant)
            .Handle(new GetMonthlyCostReportQuery(2026, 9), CancellationToken.None);

        // Carburant 100 + 60 ; entretiens 120 + 50 ; réparations 100 (l'annulée exclue)
        // + facture 300 ; autres 600 − 250 + 20.
        (fuel, maintenance, repair, other).Should().Be((160m, 170m, 400m, 370m));
        (total.Fuel, total.Maintenance, total.Repair, total.Other).Should().Be((fuel, maintenance, repair, other));
        (monthly.TotalFuelCostDzd, monthly.TotalMaintenanceCostDzd, monthly.TotalRepairCostDzd, monthly.TotalOtherCostDzd)
            .Should().Be((fuel, maintenance, repair, other));
        monthly.TotalCostDzd.Should().Be(1_100m).And.Be(total.Total);
    }
}
