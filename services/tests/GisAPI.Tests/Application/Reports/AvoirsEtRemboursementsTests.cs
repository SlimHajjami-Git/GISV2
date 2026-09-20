using FluentAssertions;
using GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Décision de Karim du 18/09/2026 sur les avoirs fournisseurs
/// (<c>credit_note</c>) et les remboursements d'assurance (<c>insurance_refund</c>),
/// tous deux enregistrés en montant POSITIF et comptés en crédit :
/// <list type="number">
///   <item>Rapports détaillés : Carburant, Entretiens, RÉPARATIONS et Autres restent
///     BRUTS ; les crédits portent une ligne à part, en négatif ; le coût total est net.</item>
///   <item>Tableaux de bord (blocs à quatre postes, sans place pour une ligne de plus) :
///     le crédit est déduit des RÉPARATIONS, et le total est le même qu'au rapport.</item>
/// </list>
/// Constat de départ (contre-relecture du 18/09/2026) : sept intitulés d'écran, la note
/// de bas de rapport et la note de pied du PDF annonçaient « Réparations, remboursements
/// d'assurance déduits » alors que le code rangeait le remboursement en « Autres » — la
/// déduction annoncée au client avait lieu ailleurs, et le code écrit pour elle était mort.
/// </summary>
public class AvoirsEtRemboursementsTests
{
    private const int CompanyId = 1;

    private static DateTime Utc(int day) => new(2026, 9, day, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DebutMois = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FinMoisExclusive = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FinMoisIncluse = new(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc);

    /// <summary>
    /// L'exemple de la règle : une réparation facturée 300 et 250 remboursés par
    /// l'assureur, plus 600 d'assurance et 150 d'avoir fournisseur. Total net 500.
    /// </summary>
    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "repair", Amount = 300m, Date = Utc(4) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "insurance_refund", Amount = 250m, Date = Utc(20) },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 1, Type = "insurance", Amount = 600m, Date = Utc(2) },
            new VehicleCost { Id = 4, CompanyId = CompanyId, VehicleId = 1, Type = "credit_note", Amount = 150m, Date = Utc(22) });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<OperatingCostReportDto> ExploitationAsync(TestGisDbContext ctx) =>
        new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

    // ═══════════════ 1. Rapports détaillés : postes bruts, ligne de crédit ═══════════════

    [Fact]
    public async Task Le_rapport_laisse_les_postes_bruts_et_sort_les_credits_sur_une_ligne()
    {
        using var ctx = await SeedAsync();

        var rapport = await ExploitationAsync(ctx);

        var v = rapport.Vehicles.Single();
        v.RepairCost.Should().Be(300m, "la réparation est affichée telle qu'elle a été facturée");
        v.OtherCost.Should().Be(600m, "l'assurance n'est pas allégée par l'avoir fournisseur");
        v.CreditAmount.Should().Be(-400m, "250 de remboursement + 150 d'avoir, en déduction");
        v.TotalCost.Should().Be(500m, "300 + 600 − 400");
        (rapport.TotalRepairCost, rapport.TotalOtherCost, rapport.TotalCreditAmount, rapport.TotalCost)
            .Should().Be((300m, 600m, -400m, 500m));
    }

    [Fact]
    public async Task Les_couts_mensuels_par_vehicule_portent_la_meme_colonne_de_credit()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetMonthlyCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyCostReportQuery(2026, 9), CancellationToken.None);

        var ligne = rapport.Vehicles.Single();
        (ligne.RepairCostDzd, ligne.OtherCostDzd, ligne.CreditAmountDzd, ligne.TotalCostDzd)
            .Should().Be((300m, 600m, -400m, 500m));
        rapport.Departments.Single().TotalCreditAmountDzd.Should().Be(-400m);
        rapport.TotalCreditAmountDzd.Should().Be(-400m);
        (rapport.TotalRepairCostDzd + rapport.TotalOtherCostDzd + rapport.TotalCreditAmountDzd)
            .Should().Be(rapport.TotalCostDzd);
    }

    [Fact]
    public async Task L_evolution_mensuelle_porte_le_credit_du_mois()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetVehicleCostEvolutionQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetVehicleCostEvolutionQuery(1, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

        var mois = rapport.Months.Single();
        (mois.RepairCost, mois.OtherCost, mois.CreditAmount, mois.TotalCost).Should().Be((300m, 600m, -400m, 500m));
        rapport.TotalCreditAmount.Should().Be(-400m);
        rapport.TotalRepairCost.Should().Be(300m);
    }

    [Fact]
    public async Task Le_rapport_mensuel_flotte_ne_compte_chaque_credit_qu_une_fois()
    {
        using var ctx = await SeedAsync();

        var rapport = await new GetMonthlyFleetReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyFleetReportQuery(2026, 9), CancellationToken.None);

        var ligne = rapport.Vehicles.Single();
        (ligne.RepairCost, ligne.OtherCost, ligne.CreditAmount, ligne.TotalCost).Should().Be((300m, 600m, -400m, 500m));
        rapport.Totals.CreditAmount.Should().Be(-400m);

        // La répartition par catégorie donne une ligne par crédit, en négatif, et la
        // somme des lignes retombe sur le total : un crédit compté deux fois (dans un
        // poste ET sur sa ligne) écarterait la somme de deux fois son montant.
        var categories = rapport.CostAnalysis.ByCategory;
        categories.Single(c => c.Category == "Remboursement assurance").Amount.Should().Be(-250m);
        categories.Single(c => c.Category == "Avoir fournisseur").Amount.Should().Be(-150m);
        categories.Single(c => c.Category == "Réparations").Amount.Should().Be(300m);
        categories.Sum(c => c.Amount).Should().Be(rapport.CostAnalysis.TotalOperationalCost).And.Be(500m);

        // Les cinq champs du bloc « Analyse des coûts » redonnent le total.
        var couts = rapport.CostAnalysis;
        (couts.FuelCost + couts.MaintenanceCost + couts.InsuranceCost + couts.OtherCosts + couts.CreditAmount)
            .Should().Be(couts.TotalOperationalCost);
        couts.CreditAmount.Should().Be(-400m);

        // Un remboursement n'est pas un passage à l'atelier : il ne doit pas gonfler
        // le nombre d'interventions ni apparaître dans la répartition par type.
        rapport.Maintenance.ByType.Should().OnlyContain(t => t.TotalCost > 0);
    }

    // ═══════════════ 2. Tableaux de bord : crédit déduit des Réparations ═══════════════

    [Fact]
    public async Task Le_tableau_de_bord_GPS_deduit_le_credit_des_reparations_et_laisse_autres_brut()
    {
        using var ctx = await SeedAsync();

        var (fuel, maintenance, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, DebutMois, FinMoisIncluse, CancellationToken.None);

        repair.Should().Be(-100m, "300 de réparation moins 250 remboursés et 150 d'avoir");
        other.Should().Be(600m, "l'assurance reste brute");
        (fuel + maintenance + repair + other).Should().Be(500m, "le même total qu'au rapport");
    }

    [Fact]
    public async Task Un_poste_de_reparations_negatif_n_est_pas_borne_a_zero()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "repair", Amount = 100m, Date = Utc(4) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "insurance_refund", Amount = 400m, Date = Utc(20) });
        await ctx.SaveChangesAsync();

        var (_, _, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, DebutMois, FinMoisIncluse, CancellationToken.None);

        // Le mois rend plus qu'il ne répare : le poste passe sous zéro et y reste.
        // Borné à 0 il aurait fait mentir le total, qui vaut bien −300.
        repair.Should().Be(-300m);
        other.Should().Be(0m);

        var aggregate = await OperatingCostAggregator.LoadAsync(
            ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object,
            DebutMois, FinMoisExclusive, null, null, CancellationToken.None);
        aggregate.Vehicles.Single().Total.RepairNetOfCredit.Should().Be(repair);
    }

    [Fact]
    public async Task Le_total_du_tableau_de_bord_est_identique_a_celui_du_rapport()
    {
        using var ctx = await SeedAsync();

        var rapport = await ExploitationAsync(ctx);
        var (fuel, maintenance, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, DebutMois, FinMoisIncluse, CancellationToken.None);
        var aggregate = await OperatingCostAggregator.LoadAsync(
            ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object,
            DebutMois, FinMoisExclusive, null, null, CancellationToken.None);
        var seaux = aggregate.Vehicles.Single().Total;

        (fuel + maintenance + repair + other).Should().Be(rapport.TotalCost).And.Be(seaux.Total);
        // Le seul écart entre les deux présentations est l'endroit où vit le crédit.
        repair.Should().Be(rapport.TotalRepairCost + rapport.TotalCreditAmount);
        other.Should().Be(rapport.TotalOtherCost);
    }

    // ═══════════════ 3. Robustesse sur les données anciennes ═══════════════

    [Fact]
    public async Task Un_avoir_ancien_saisi_en_negatif_reste_un_credit()
    {
        // Avant le refus des montants négatifs (DEF-050) un avoir pouvait être saisi
        // à −120 : compté « signe × montant », il aurait rendu le seau des crédits
        // POSITIF, donc une ligne « Avoirs et remboursements » qui ALOURDIT le coût.
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "fuel", Amount = 500m, Date = Utc(3) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "avoir", Amount = -120m, Date = Utc(5) },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 1, Type = "avoir", Amount = 30m, Date = Utc(6) });
        await ctx.SaveChangesAsync();

        var aggregate = await OperatingCostAggregator.LoadAsync(
            ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object,
            DebutMois, FinMoisExclusive, null, null, CancellationToken.None);
        var seaux = aggregate.Vehicles.Single().Total;
        (seaux.Fuel, seaux.Other, seaux.Credit, seaux.Total).Should().Be((500m, 0m, -150m, 350m));

        var (fuel, _, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, DebutMois, FinMoisIncluse, CancellationToken.None);
        (fuel, repair, other).Should().Be((500m, -150m, 0m));
    }
}
