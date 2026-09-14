using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Recette du 11/09/2026 : le « Rapport mensuel flotte » était vide pour un
/// compte sans boîtier GPS (Calypso GPA) — son tableau par véhicule ne listait
/// que les véhicules équipés, et plusieurs indicateurs GPS s'affichaient à 0
/// comme s'ils étaient mesurés (« taux d'utilisation 0 % — réduire la flotte »).
/// </summary>
public class MonthlyFleetSansGpsTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static Task<MonthlyFleetReportDto> RunAsync(TestGisDbContext ctx) =>
        new GetMonthlyFleetReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyFleetReportQuery(2026, 8), CancellationToken.None);

    /// <summary>
    /// Août 2026, trois véhicules SANS boîtier :
    ///   - « Service 01 » : 2 pleins, 61 495 → 62 275 km (780 km), 75,6 L ;
    ///   - « Logistique 01 » : 2 pleins 208 294 → 209 572 km et une réparation
    ///     de 468 € relevée à 208 152 km le 03/08 (1 420 km), 150 L ;
    ///   - « Terrain 01 » : un seul plein, 40 L — un relevé ne fait pas une distance.
    /// </summary>
    private static async Task<TestGisDbContext> SeedGpaAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 2, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 3, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "available" });
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(8, 5), Volume = 37.8m, TotalAmount = 69.55m, OdometerKm = 61_495 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(8, 20), Volume = 37.8m, TotalAmount = 69.55m, OdometerKm = 62_275 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Utc(8, 10), Volume = 75, TotalAmount = 129, OdometerKm = 208_294 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Utc(8, 24), Volume = 75, TotalAmount = 129, OdometerKm = 209_572 },
            new FuelEntry { VehicleId = 3, CompanyId = CompanyId, InvoiceDate = Utc(8, 12), Volume = 40, TotalAmount = 68.8m, OdometerKm = 168_364 });
        ctx.Repairs.Add(new Repair
        {
            Id = 31, SocieteId = CompanyId, VehicleId = 2, Reference = "REP-31", Description = "Nettoyage FAP forcé",
            RepairType = "mecanique", RepairDate = Utc(8, 3), TotalCost = 468, MileageAtRepair = 208_152, Status = "completed"
        });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Un_parc_sans_boitier_a_une_ligne_par_vehicule_avec_le_kilometrage_des_saisies()
    {
        using var ctx = await SeedGpaAsync();

        var rapport = await RunAsync(ctx);

        rapport.FleetHasGps.Should().BeFalse();
        rapport.Vehicles.Should().HaveCount(3, "une ligne par véhicule, même sans boîtier");
        rapport.Utilization.ByVehicle.Should().BeEmpty("l'utilisation reste réservée aux véhicules équipés");

        var service = rapport.Vehicles.Single(v => v.Plate == "GA-214-RK");
        service.HasGps.Should().BeFalse();
        service.DistanceSource.Should().Be("odometer");
        service.DistanceKm.Should().Be(780);
        service.Liters.Should().Be(75.6);
        service.ConsumptionPer100Km.Should().Be(9.69);
        service.UtilizationRate.Should().BeNull("sans boîtier, l'utilisation n'est pas mesurée");
        service.Trips.Should().BeNull();

        var logistique = rapport.Vehicles.Single(v => v.Plate == "GH-619-XC");
        logistique.DistanceKm.Should().Be(1_420, "le relevé de la réparation compte aussi (208 152 → 209 572)");
        logistique.RepairCost.Should().Be(468m);
        logistique.FuelCost.Should().Be(258m);
        logistique.TotalCost.Should().Be(726m);
        logistique.CostPerKm.Should().Be(Math.Round(726m / 1_420m, 3));

        var terrain = rapport.Vehicles.Single(v => v.Plate == "GG-852-BD");
        terrain.DistanceSource.Should().Be("none", "un seul relevé ne donne pas de distance");
        terrain.DistanceKm.Should().BeNull();
        terrain.ConsumptionPer100Km.Should().BeNull("0 L/100 km serait faux");
        terrain.CostPerKm.Should().BeNull();

        rapport.Vehicles.First().Plate.Should().Be("GH-619-XC", "le plus coûteux d'abord");

        rapport.Totals.DistanceKm.Should().Be(2_200);
        rapport.Totals.MeasuredVehicles.Should().Be(2);
        rapport.Totals.Liters.Should().Be(265.6);
        rapport.Totals.ConsumptionPer100Km.Should().Be(Math.Round((75.6 + 150) / 2_200 * 100, 2),
            "les 40 L du véhicule sans kilométrage n'entrent pas dans la moyenne");
        rapport.Totals.TotalCost.Should().Be(rapport.CostAnalysis.TotalOperationalCost);
        rapport.Totals.CostPerKm.Should().Be(Math.Round((139.10m + 726m) / 2_200m, 3),
            "coût des seuls véhicules mesurés : les 68,80 € du véhicule sans kilométrage n'entrent pas au numérateur");
        rapport.ExecutiveSummary.TotalDistanceKm.Should().Be(2_200, "elle valait 0 faute de boîtier");
    }

    [Fact]
    public async Task La_repartition_des_couts_retombe_sur_le_total()
    {
        using var ctx = await SeedGpaAsync();
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600, Date = Utc(8, 2) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "assurance", Amount = 50, Date = Utc(8, 3) },
            // Facture de sinistre : déjà dans « Réparations » pour l'agrégateur.
            new VehicleCost { VehicleId = 2, CompanyId = CompanyId, Type = "repair", Amount = 300, Date = Utc(8, 12) },
            // Remboursement : un crédit.
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance_refund", Amount = 250, Date = Utc(8, 20) });
        await ctx.SaveChangesAsync();

        var rapport = await RunAsync(ctx);

        var categories = rapport.CostAnalysis.ByCategory;
        categories.Sum(c => c.Amount).Should().Be(rapport.CostAnalysis.TotalOperationalCost);
        categories.Should().NotContain(c => c.Category.Contains("dépense"), "la dépense « repair » est déjà dans Réparations");
        categories.Single(c => c.Category == "Réparations").Amount.Should().Be(768m, "468 à l'atelier + 300 de facture");
        categories.Should().ContainSingle(c => c.Category == "Assurance").Which.Amount.Should().Be(650m);
        categories.Single(c => c.Category == "Remboursement assurance").Amount.Should().Be(-250m);
        rapport.Totals.TotalCost.Should().Be(rapport.CostAnalysis.TotalOperationalCost);
    }

    [Fact]
    public async Task Un_vehicule_equipe_prend_la_distance_de_ses_trajets_et_jamais_des_litres_estimes()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.GpsDevices.Add(new GpsDevice { Id = 7, DeviceUid = "DEV-7", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 9, Name = "Camion", Plate = "GJ-473-KS", CompanyId = CompanyId, Status = "available", Type = "berline", GpsDeviceId = 7 });
        ctx.Trips.AddRange(
            new Trip { CompanyId = CompanyId, VehicleId = 9, StartTime = Utc(8, 8), DistanceKm = 300, DurationMinutes = 240, Status = "completed" },
            new Trip { CompanyId = CompanyId, VehicleId = 9, StartTime = Utc(8, 22), DistanceKm = 200, DurationMinutes = 180, Status = "completed" });
        await ctx.SaveChangesAsync();

        var rapport = await RunAsync(ctx);

        var camion = rapport.Vehicles.Single();
        camion.DistanceSource.Should().Be("gps");
        camion.DistanceKm.Should().Be(500, "les trajets terminés, comme les rapports de coûts (aucune position : 0 km avant)");
        camion.Liters.Should().Be(0, "aucun plein : l'estimation au taux du type de véhicule n'est pas une mesure");
        camion.ConsumptionPer100Km.Should().BeNull();
        rapport.Totals.Liters.Should().Be(0);
        rapport.Totals.ConsumptionPer100Km.Should().BeNull();
        ctx.Dispose();
    }

    [Fact]
    public async Task Un_parc_sans_boitier_n_affiche_plus_d_indicateur_gps_trompeur()
    {
        using var ctx = await SeedGpaAsync();

        var rapport = await RunAsync(ctx);

        rapport.KeyPerformanceIndicators.Select(k => k.Category)
            .Should().NotContain(new[] { "Utilisation", "Conducteurs" });
        rapport.KeyPerformanceIndicators.Select(k => k.Category)
            .Should().Contain(new[] { "Carburant", "Coûts" });
        rapport.ExecutiveSummary.KeyInsights.Should().NotContain(i => i.Contains("Taux d'utilisation"));
        rapport.ExecutiveSummary.KeyInsights.Should().Contain(i => i.Contains("Aucun boîtier GPS") && i.Contains("2/3"));
        rapport.ExecutiveSummary.Recommendations.Should().NotContain(r => r.Contains("réduction de la taille"));
        rapport.Efficiency.Metrics.Should().BeEmpty("« temps d'inactivité 0 %, dans l'objectif » n'était pas mesuré");
        rapport.Alerts.Should().NotContain(a => a.Type == "LowUtilization");
    }

    [Fact]
    public async Task Un_parc_mixte_garde_la_distance_gps_des_vehicules_equipes()
    {
        using var ctx = await SeedGpaAsync();
        ctx.GpsDevices.Add(new GpsDevice { Id = 5, DeviceUid = "DEV-5", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 4, Name = "Camion", Plate = "GJ-473-KS", CompanyId = CompanyId, Status = "available", GpsDeviceId = 5 });
        for (var day = 1; day <= 5; day++)
        {
            ctx.GpsPositions.Add(new GpsPosition
            {
                Id = day, DeviceId = 5, SpeedKph = 60,
                Latitude = 36.8 + day * 0.05, Longitude = 10.1 + day * 0.05,
                RecordedAt = new DateTime(2026, 8, day, 10, 0, 0, DateTimeKind.Utc).AddHours(1)
            });
        }
        await ctx.SaveChangesAsync();

        var rapport = await RunAsync(ctx);

        rapport.FleetHasGps.Should().BeTrue();
        rapport.VehiclesWithGps.Should().Be(1);
        rapport.Vehicles.Should().HaveCount(4);

        var camion = rapport.Vehicles.Single(v => v.Plate == "GJ-473-KS");
        var gps = rapport.Utilization.ByVehicle.Single(u => u.VehicleId == 4);
        camion.HasGps.Should().BeTrue();
        camion.DistanceSource.Should().Be("gps");
        camion.DistanceKm.Should().Be(gps.TotalDistanceKm, "la distance GPS du rapport ne change pas");
        camion.UtilizationRate.Should().Be(gps.UtilizationRate);
        camion.Trips.Should().Be(gps.TotalTrips);

        rapport.Vehicles.Single(v => v.Plate == "GA-214-RK").DistanceSource.Should().Be("odometer");
        rapport.ExecutiveSummary.TotalDistanceKm.Should().Be(Math.Round(gps.TotalDistanceKm + 2_200, 2),
            "distance GPS du véhicule équipé + kilométrage reconstitué des autres");
        rapport.KeyPerformanceIndicators.Select(k => k.Category).Should().Contain("Utilisation");

        // Utilisation rapportée au seul véhicule équipé : les trois véhicules sans
        // boîtier ne sont pas « inutilisés », ils ne sont pas mesurés.
        rapport.Utilization.OverallUtilizationRate.Should().Be(camion.UtilizationRate!.Value);
    }
}
