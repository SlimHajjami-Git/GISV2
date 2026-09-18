using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Recette du 11/09/2026 sur « Évolution des coûts » et l'agrégateur partagé
/// par les quatre rapports de coûts.
/// </summary>
public class CostEvolutionRecetteTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique", Plate = "GH-619-XC", CompanyId = CompanyId });
        // Trois mois : juillet et août complets, septembre arrêté au 10.
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(7, 5), Volume = 60, TotalAmount = 900, OdometerKm = 10_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(8, 5), Volume = 60, TotalAmount = 800, OdometerKm = 12_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(9, 5), Volume = 10, TotalAmount = 100, OdometerKm = 12_500 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    // ══════════════ Le mois incomplet ══════════════

    [Fact]
    public async Task Le_mois_incomplet_est_signale_et_n_est_jamais_le_moins_eleve()
    {
        using var ctx = await SeedAsync();

        // Période arrêtée au 10 septembre : septembre n'a que 10 jours.
        var rapport = await new GetVehicleCostEvolutionQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetVehicleCostEvolutionQuery(1, new DateTime(2026, 7, 1), new DateTime(2026, 9, 10)), CancellationToken.None);

        var septembre = rapport.Months.Single(m => m.Month == 9);
        septembre.IsPartial.Should().BeTrue("la période s'arrête le 10 septembre");
        septembre.VariationPct.Should().BeNull("comparer dix jours à un mois entier n'a pas de sens");

        rapport.Months.Where(m => m.Month != 9).Should().OnlyContain(m => !m.IsPartial);

        rapport.LowestMonth.Should().NotBeNull();
        rapport.LowestMonth!.Month.Should().Be(8,
            "août (800) est le moins élevé des mois COMPLETS ; septembre (100) est tronqué");
        rapport.HighestMonth!.Month.Should().Be(7);
    }

    [Fact]
    public async Task Une_periode_qui_couvre_le_mois_entier_n_a_aucun_mois_incomplet()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetVehicleCostEvolutionQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetVehicleCostEvolutionQuery(1, new DateTime(2026, 7, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

        rapport.Months.Should().OnlyContain(m => !m.IsPartial);
        rapport.LowestMonth!.Month.Should().Be(9, "septembre complet redevient un candidat comme les autres");
    }

    [Fact]
    public async Task Un_premier_mois_tronque_par_une_periode_personnalisee_est_incomplet_et_ne_sert_pas_de_base()
    {
        using var ctx = await SeedAsync();
        // Juin : 200 le 20 (dans la période) et 5 000 le 5 (avant son début).
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(6, 5), Volume = 300, TotalAmount = 5_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(6, 20), Volume = 12, TotalAmount = 200 });
        await ctx.SaveChangesAsync();

        // Constat du 14/09/2026 : du 15/06 au 31/08, juin ne couvre que 16 jours
        // mais n'était pas signalé — « mois le moins élevé » et hausse de juillet
        // calculée contre un demi-mois (+350 %).
        var rapport = await new GetVehicleCostEvolutionQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetVehicleCostEvolutionQuery(1, new DateTime(2026, 6, 15), new DateTime(2026, 8, 31)), CancellationToken.None);

        var juin = rapport.Months.Single(m => m.Month == 6);
        juin.IsPartial.Should().BeTrue("la période commence le 15 juin");
        juin.TotalCost.Should().Be(200m);
        juin.VariationPct.Should().BeNull();

        var juillet = rapport.Months.Single(m => m.Month == 7);
        juillet.IsPartial.Should().BeFalse();
        juillet.VariationPct.Should().BeNull("juillet ne se compare pas à un demi-mois de juin");

        rapport.Months.Single(m => m.Month == 8).VariationPct.Should().Be(Math.Round((800m - 900m) / 900m * 100m, 1));
        rapport.LowestMonth!.Month.Should().Be(8, "juin (200) est tronqué : août (800) est le moins élevé des mois complets");
        rapport.HighestMonth!.Month.Should().Be(7);
    }

    // ══════════════ La ventilation des dépenses ══════════════

    [Fact]
    public async Task Une_depense_de_categorie_reparation_va_dans_les_reparations()
    {
        using var ctx = await SeedAsync();
        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 450, Date = Utc(8, 12) });
        await ctx.SaveChangesAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 7, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

        var v = rapport.Vehicles.Single();
        v.RepairCost.Should().Be(450m, "« Réparation » est une catégorie de l'écran Dépenses");
        v.OtherCost.Should().Be(0m, "elle ne doit plus tomber dans « Autres »");
    }

    [Fact]
    public async Task Un_remboursement_d_assurance_allege_le_cout_au_lieu_de_le_gonfler()
    {
        using var ctx = await SeedAsync();
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600, Date = Utc(8, 1) },
            // Le module Sinistres enregistre le remboursement en montant POSITIF.
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance_refund", Amount = 250, Date = Utc(8, 20) });
        await ctx.SaveChangesAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 7, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

        var v = rapport.Vehicles.Single();
        // Décision du 18/09/2026 : « Autres » reste BRUT et le remboursement porte sa
        // propre ligne, en négatif. Seul le coût total est net — et il n'a pas changé.
        v.OtherCost.Should().Be(600m, "l'assurance est affichée telle qu'elle a été facturée");
        v.CreditAmount.Should().Be(-250m, "le remboursement est une ligne de crédit, pas une remise sur l'assurance");
        v.TotalCost.Should().Be(900m + 800m + 100m + 350m);
    }
}
