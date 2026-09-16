using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Recette Calypso GPA du 13/09/2026 — « deux écrans, deux chiffres » sur les
/// coûts. Chaque rapport avait fini par se réécrire ses propres règles ;
/// <see cref="GisAPI.Application.Features.Reports.Common.OperatingCostAggregator"/>
/// est la définition de référence et ces tests vérifient que les autres s'y
/// tiennent : réparation annulée exclue, remboursement d'assurance déduit, mois
/// tronqué signalé par l'une OU l'autre borne, un seul coût au kilomètre.
/// </summary>
public class ChiffresCoutsRecetteTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 10, 0, 0, DateTimeKind.Utc);

    // ══════════════ « Coûts mensuel par véhicule » (DEF-018, DEF-023) ══════════════

    /// <summary>
    /// Septembre 2026, un véhicule sans boîtier : deux pleins (1 000 km, 300 €),
    /// une réparation de 245 € à l'atelier, une SECONDE réparation de 80 €
    /// annulée, une facture de sinistre de 300 € saisie en dépense « repair »,
    /// une assurance de 600 € et 100 € remboursés par l'assureur.
    /// </summary>
    private static async Task<TestGisDbContext> SeedSeptembreAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 4), Volume = 80, TotalAmount = 150, OdometerKm = 100_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 24), Volume = 80, TotalAmount = 150, OdometerKm = 101_000 });
        ctx.Repairs.AddRange(
            new Repair
            {
                Id = 60, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-60", Description = "Embrayage",
                RepairType = "mecanique", RepairDate = Utc(2026, 9, 10), TotalCost = 245, Status = "completed"
            },
            new Repair
            {
                Id = 61, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-61", Description = "Intervention annulée",
                RepairType = "mecanique", RepairDate = Utc(2026, 9, 12), TotalCost = 80, Status = "cancelled"
            });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 300, Date = Utc(2026, 9, 14) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600, Date = Utc(2026, 9, 2) },
            // Le module Sinistres enregistre le remboursement en montant POSITIF.
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance_refund", Amount = 100, Date = Utc(2026, 9, 20) });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<MonthlyCostReportDto> MensuelAsync(TestGisDbContext ctx) =>
        new GetMonthlyCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyCostReportQuery(2026, 9), CancellationToken.None);

    private static Task<OperatingCostReportDto> ExploitationAsync(TestGisDbContext ctx) =>
        new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetOperatingCostReportQuery(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)), CancellationToken.None);

    [Fact]
    public async Task Une_reparation_annulee_ne_compte_plus_dans_le_rapport_mensuel()
    {
        using var ctx = await SeedSeptembreAsync();

        var rapport = await MensuelAsync(ctx);

        var ligne = rapport.Vehicles.Single();
        ligne.RepairCostDzd.Should().Be(545m,
            "245 à l'atelier + 300 de facture de sinistre ; les 80 de l'intervention annulée ne sont dus à personne");
        rapport.TotalRepairCostDzd.Should().Be(545m);
    }

    [Fact]
    public async Task Le_remboursement_d_assurance_est_deduit_du_rapport_mensuel()
    {
        using var ctx = await SeedSeptembreAsync();

        var rapport = await MensuelAsync(ctx);

        var ligne = rapport.Vehicles.Single();
        ligne.OtherCostDzd.Should().Be(500m, "600 d'assurance moins 100 remboursés, et non 700");
        rapport.TotalOtherCostDzd.Should().Be(500m);
    }

    [Fact]
    public async Task Le_rapport_mensuel_et_le_cout_d_exploitation_annoncent_les_memes_totaux()
    {
        using var ctx = await SeedSeptembreAsync();

        var mensuel = await MensuelAsync(ctx);
        var exploitation = await ExploitationAsync(ctx);

        mensuel.TotalFuelCostDzd.Should().Be(exploitation.TotalFuelCost);
        mensuel.TotalMaintenanceCostDzd.Should().Be(exploitation.TotalMaintenanceCost);
        mensuel.TotalRepairCostDzd.Should().Be(exploitation.TotalRepairCost);
        mensuel.TotalOtherCostDzd.Should().Be(exploitation.TotalOtherCost);
        mensuel.TotalCostDzd.Should().Be(exploitation.TotalCost,
            "le client lisait 3 626,13 d'un côté et 3 426,13 de l'autre pour le même mois");
    }

    // ══════════════ Distance non mesurée (DEF-011) ══════════════

    /// <summary>
    /// Septembre 2026, deux véhicules sans boîtier : GL-694-PN n'a qu'UN relevé
    /// dans le mois (sa vidange du 11/09 à 98 730 km, notée deux fois le même
    /// jour), GA-214-RK en a deux (50 000 puis 51 000) et paie, en plus de ses
    /// 200 € de carburant, un entretien de 150 €.
    /// </summary>
    private static async Task<TestGisDbContext> SeedUnSeulReleveAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 42, Name = "Service 42", Plate = "GL-694-PN", CompanyId = CompanyId },
            new Vehicle { Id = 43, Name = "Service 43", Plate = "GA-214-RK", CompanyId = CompanyId });
        // Le même passage à l'atelier, noté par l'entretien ET par la dépense qui
        // le facture : un seul relevé, pas deux.
        ctx.MaintenanceLogs.Add(new MaintenanceLog
        {
            Id = 1, VehicleId = 42, CompanyId = CompanyId, TemplateId = 1,
            DoneDate = Utc(2026, 9, 11), DoneKm = 98_730, ActualCost = 120
        });
        ctx.VehicleCosts.AddRange(
            new VehicleCost
            {
                VehicleId = 42, CompanyId = CompanyId, Type = "maintenance", Amount = 120,
                Date = Utc(2026, 9, 11), Mileage = 98_730
            },
            // Entretien d'un véhicule MESURÉ : sans lui, la vue carburant ne
            // retirait rien des ratios et ne prouvait pas qu'elle les recalcule.
            new VehicleCost
            {
                VehicleId = 43, CompanyId = CompanyId, Type = "maintenance", Amount = 150,
                Date = Utc(2026, 9, 12)
            });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 43, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 5), Volume = 50, TotalAmount = 100, OdometerKm = 50_000 },
            new FuelEntry { VehicleId = 43, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 20), Volume = 50, TotalAmount = 100, OdometerKm = 51_000 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Un_seul_releve_dans_le_mois_ne_mesure_aucune_distance()
    {
        using var ctx = await SeedUnSeulReleveAsync();

        var rapport = await MensuelAsync(ctx);

        var ligne = rapport.Vehicles.Single(v => v.VehicleId == 42);
        ligne.Km.Should().BeNull("un relevé isolé ne permet aucun écart : 0 km se lisait comme une mesure");
        ligne.KmSource.Should().Be("none");
        ligne.CostPerKm.Should().BeNull("un coût au km sans distance mesurée est un chiffre inventé");
        ligne.ConsumptionPer100Km.Should().BeNull();
        ligne.MaintenanceRepairPer100Km.Should().BeNull();
        ligne.MaintenanceCostDzd.Should().Be(120m, "la dépense, elle, reste affichée");
    }

    [Fact]
    public async Task Deux_releves_dans_le_mois_mesurent_la_distance_et_les_ratios()
    {
        using var ctx = await SeedUnSeulReleveAsync();

        var rapport = await MensuelAsync(ctx);

        var ligne = rapport.Vehicles.Single(v => v.VehicleId == 43);
        ligne.Km.Should().Be(1_000m);
        ligne.KmSource.Should().Be("odometer");
        ligne.ConsumptionPer100Km.Should().Be(10m, "100 litres sur 1 000 km");
        rapport.TotalKm.Should().Be(1_000m, "seules les distances mesurées entrent dans le total");
    }

    [Fact]
    public async Task Les_ratios_du_departement_et_du_rapport_ecartent_les_vehicules_non_mesures()
    {
        using var ctx = await SeedUnSeulReleveAsync();

        var rapport = await MensuelAsync(ctx);

        // 470 € de dépenses pour 1 000 km mesurés, mais les 120 € d'entretien de
        // GL-694-PN n'ont aucun kilomètre en face : les compter annonçait 0,47 €/km.
        rapport.CostPerKm.Should().Be(0.35m, "200 € de carburant et 150 € d'entretien sur les 1 000 km de GA-214-RK");
        rapport.FuelPer100Km.Should().Be(20m);
        rapport.MaintenanceRepairPer100Km.Should().Be(15m);
        rapport.ConsumptionPer100Km.Should().Be(10m);
        rapport.ConsumptionPrPer100Km.Should().BeNull("aucun kilomètre mesuré en août");

        var departement = rapport.Departments.Single();
        departement.CostPerKm.Should().Be(0.35m);
        departement.MaintenanceRepairPer100Km.Should().Be(15m);
        departement.ConsumptionPer100Km.Should().Be(10m);

        // La vue carburant retire l'entretien de GA-214-RK : ses ratios doivent être
        // RECALCULÉS, pas hérités de la vue complète (0,35 €/km, 15 aux 100 km).
        var carburant = rapport.ToFuelOnly();
        carburant.CostPerKm.Should().Be(0.2m, "200 € de carburant seul sur 1 000 km");
        carburant.MaintenanceRepairPer100Km.Should().Be(0m);
        carburant.ConsumptionPer100Km.Should().Be(10m);
        carburant.Departments.Single().CostPerKm.Should().Be(0.2m);
        carburant.Departments.Single().MaintenanceRepairPer100Km.Should().Be(0m);
        var ligne = carburant.Vehicles.Single(v => v.VehicleId == 43);
        ligne.CostPerKm.Should().Be(0.2m);
        ligne.MaintenanceRepairPer100Km.Should().Be(0m);
    }

    /// <summary>
    /// Un véhicule sans boîtier passé à l'atelier à chaque relevé noté (60 € par
    /// passage). La règle « non mesuré » du rapport mensuel doit être EXACTEMENT
    /// celle de « Coût d'exploitation réel » (OperatingCostAggregator) : sans
    /// kilomètre additionné, pas de distance.
    /// </summary>
    private static async Task<TestGisDbContext> SeedRelevesAsync(params int[] releves)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 44, Name = "Service 44", Plate = "GG-852-BD", CompanyId = CompanyId });
        for (var i = 0; i < releves.Length; i++)
            ctx.VehicleCosts.Add(new VehicleCost
            {
                VehicleId = 44, CompanyId = CompanyId, Type = "maintenance", Amount = 60,
                Date = Utc(2026, 9, 4 + 7 * i), Mileage = releves[i]
            });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Des_releves_identiques_ne_mesurent_aucune_distance()
    {
        // Deux passages à l'atelier au même compteur : aucun kilomètre additionné,
        // la référence rend « non mesuré » et non 0 km.
        using var ctx = await SeedRelevesAsync(12_000, 12_000);

        var rapport = await MensuelAsync(ctx);

        var ligne = rapport.Vehicles.Single();
        ligne.Km.Should().BeNull("« Coût d'exploitation réel » ne mesure aucune distance sur ces relevés");
        ligne.KmSource.Should().Be("none");
        ligne.CostPerKm.Should().BeNull();
        ligne.MaintenanceCostDzd.Should().Be(120m, "la dépense, elle, reste affichée");
        rapport.TotalKm.Should().Be(0m);
        rapport.CostPerKm.Should().BeNull("le groupe n'a aucun kilomètre mesuré à rapporter");
        rapport.Departments.Single().CostPerKm.Should().BeNull();
    }

    [Theory]
    [InlineData(new[] { 12_000 })]                  // relevé isolé
    [InlineData(new[] { 12_000, 12_000 })]          // relevés identiques
    [InlineData(new[] { 12_000, 8_000 })]           // seul écart : un recul de compteur, donc une rupture
    [InlineData(new[] { 12_000, 12_000, 12_450 })]  // écart nul puis 450 km
    public async Task Le_rapport_mensuel_mesure_la_distance_comme_le_cout_d_exploitation(int[] releves)
    {
        using var ctx = await SeedRelevesAsync(releves);

        var mensuel = (await MensuelAsync(ctx)).Vehicles.Single();
        var reference = (await ExploitationAsync(ctx)).Vehicles.Single();

        mensuel.Km.Should().Be(reference.DistanceKm);
        mensuel.KmSource.Should().Be(reference.DistanceSource);
    }

    // ══════════════ « Évolution des coûts » (DEF-022) ══════════════

    /// <summary>
    /// Octobre 2025 vaut 390 sur le mois entier mais 190 à partir du 15 ;
    /// novembre 577, décembre 557. Une période qui démarre le 15/10 élisait donc
    /// octobre « mois le moins élevé » à un montant qui n'existe pas.
    /// </summary>
    private static async Task<TestGisDbContext> SeedTroisMoisAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 39, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 39, CompanyId = CompanyId, InvoiceDate = Utc(2025, 10, 5), Volume = 100, TotalAmount = 200 },
            new FuelEntry { VehicleId = 39, CompanyId = CompanyId, InvoiceDate = Utc(2025, 10, 20), Volume = 95, TotalAmount = 190 },
            new FuelEntry { VehicleId = 39, CompanyId = CompanyId, InvoiceDate = Utc(2025, 11, 12), Volume = 288, TotalAmount = 577 },
            new FuelEntry { VehicleId = 39, CompanyId = CompanyId, InvoiceDate = Utc(2025, 12, 12), Volume = 278, TotalAmount = 557 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<VehicleCostEvolutionDto> EvolutionAsync(TestGisDbContext ctx, DateTime debut, DateTime fin) =>
        new GetVehicleCostEvolutionQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetVehicleCostEvolutionQuery(39, debut, fin), CancellationToken.None);

    [Fact]
    public async Task Un_mois_tronque_par_la_date_de_debut_est_signale_et_ne_concourt_pas()
    {
        using var ctx = await SeedTroisMoisAsync();

        var rapport = await EvolutionAsync(ctx, new DateTime(2025, 10, 15), new DateTime(2025, 12, 31));

        var octobre = rapport.Months.Single(m => m.Month == 10);
        octobre.IsPartial.Should().BeTrue("la période démarre le 15 octobre");
        octobre.TotalCost.Should().Be(190m, "seuls les pleins à partir du 15 sont dans la période");
        octobre.VariationPct.Should().BeNull();

        rapport.Months.Single(m => m.Month == 11).VariationPct.Should().BeNull(
            "un mois entier comparé au mois tronqué qui le précède affiche une flambée aussi fausse");
        rapport.Months.Single(m => m.Month == 12).VariationPct.Should().NotBeNull(
            "décembre et novembre sont deux mois complets : la comparaison a un sens");

        rapport.LowestMonth.Should().NotBeNull();
        rapport.LowestMonth!.Month.Should().Be(12,
            "décembre (557) est le moins élevé des mois COMPLETS ; octobre est tronqué");
    }

    [Fact]
    public async Task Une_periode_alignee_sur_les_mois_entiers_n_a_aucun_mois_tronque()
    {
        using var ctx = await SeedTroisMoisAsync();

        var rapport = await EvolutionAsync(ctx, new DateTime(2025, 10, 1), new DateTime(2025, 12, 31));

        rapport.Months.Should().OnlyContain(m => !m.IsPartial);
        rapport.Months.Single(m => m.Month == 10).TotalCost.Should().Be(390m);
        rapport.LowestMonth!.Month.Should().Be(10, "octobre entier (390) redevient un candidat comme les autres");
    }

    // ══════════════ « Rapport mensuel flotte » (DEF-025) ══════════════

    [Fact]
    public async Task Le_rapport_mensuel_flotte_n_annonce_qu_un_seul_cout_au_kilometre()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" },
            // Sans second relevé, ce véhicule n'a pas de kilométrage : son coût ne
            // doit entrer NI au numérateur NI au dénominateur du coût au km.
            new Vehicle { Id = 2, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "available" });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 5), Volume = 50, TotalAmount = 100, OdometerKm = 50_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 20), Volume = 50, TotalAmount = 100, OdometerKm = 51_000 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Utc(2026, 9, 12), Volume = 40, TotalAmount = 500, OdometerKm = 168_364 });
        await ctx.SaveChangesAsync();

        var rapport = await new GetMonthlyFleetReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyFleetReportQuery(2026, 9), CancellationToken.None);
        var exploitation = await ExploitationAsync(ctx);

        rapport.Totals.CostPerKm.Should().Be(0.2m, "200 € sur 1 000 km mesurés");
        rapport.CostAnalysis.CostPerKm.Should().Be(rapport.Totals.CostPerKm!.Value,
            "le même document annonçait 1,271 dans l'analyse des coûts et 0,79 dans ses totaux");
        rapport.CostAnalysis.CostPerKm.Should().Be(exploitation.AverageCostPerKm!.Value,
            "« Coût d'exploitation réel » est la définition de référence");

        var kpi = rapport.KeyPerformanceIndicators.Single(k => k.Name == "Coût par kilomètre");
        kpi.Value.Should().Be((double)rapport.Totals.CostPerKm!.Value, "le KPI affiché contredisait le tableau du même rapport");
        ctx.Dispose();
    }
}
