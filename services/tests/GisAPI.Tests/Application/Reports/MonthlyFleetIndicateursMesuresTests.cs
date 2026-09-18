using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Rapport mensuel flotte, campagne de test Calypso GPA :
/// <list type="bullet">
///   <item>DEF-024 — pour un parc sans boîtier, les indicateurs que seul le GPS
///     produit s'affichaient à 0 comme des mesures (« 0 trajet, 0 jour
///     d'activité, 31 jours d'inactivité, Distance 0 → 0 stable ») à côté d'un
///     tableau qui comptait 28 729 km.</item>
///   <item>DEF-026 — les alertes « Consommation élevée » divisaient les litres
///     achetés dans le mois par le kilométrage relevé dans le mois : un seul plein
///     de 81 L rapporté aux 142 km relevés ensuite donnait « 57 L/100 km ».</item>
/// </list>
/// </summary>
public class MonthlyFleetIndicateursMesuresTests
{
    private const int CompanyId = 1;
    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static Task<MonthlyFleetReportDto> RunAsync(TestGisDbContext ctx, int month) =>
        new GetMonthlyFleetReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetMonthlyFleetReportQuery(2026, month), CancellationToken.None);

    private static FuelEntry Plein(int vehicleId, int month, int day, decimal litres, long km) => new()
    {
        VehicleId = vehicleId, CompanyId = CompanyId, InvoiceDate = Utc(month, day),
        Volume = litres, TotalAmount = litres * 1.8m, OdometerKm = km
    };

    // ==================== DEF-024 ====================

    /// <summary>
    /// Parc GPA, août 2026 (juillet pour la comparaison) :
    ///   - « Service 01 » (Berline, disponible) : 780 km en août, 800 en juillet ;
    ///   - « Logistique 01 » (Fourgon, en service) : 1 278 km en août, 1 000 en juillet ;
    ///   - « Terrain 01 » (SUV, hors service) : un seul plein en août, non mesuré.
    /// </summary>
    private static async Task<TestGisDbContext> ParcGpaAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available", Type = "Berline" },
            new Vehicle { Id = 2, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId, Status = "in_use", Type = "Fourgon" },
            new Vehicle { Id = 3, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "out_of_service", Type = "SUV" });
        ctx.FuelEntries.AddRange(
            Plein(1, 7, 3, 40, 60_500), Plein(1, 7, 25, 40, 61_300),
            Plein(2, 7, 12, 70, 207_000), Plein(2, 7, 28, 70, 208_000),
            Plein(1, 8, 5, 37.8m, 61_495), Plein(1, 8, 20, 37.8m, 62_275),
            Plein(2, 8, 10, 75, 208_294), Plein(2, 8, 24, 75, 209_572),
            Plein(3, 8, 12, 40, 168_364));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Sans_boitier_les_indicateurs_gps_sont_non_mesures_et_non_nuls()
    {
        using var ctx = await ParcGpaAsync();

        var rapport = await RunAsync(ctx, 8);

        rapport.FleetHasGps.Should().BeFalse();
        rapport.Totals.DistanceKm.Should().Be(2_058);

        var resume = rapport.ExecutiveSummary;
        resume.TotalDistanceKm.Should().Be(2_058);
        resume.TotalTrips.Should().BeNull("0 trajet se lisait comme une mesure");
        resume.FleetUtilizationRate.Should().BeNull();
        resume.TotalDrivingHours.Should().BeNull();

        var utilisation = rapport.Utilization;
        utilisation.OverallUtilizationRate.Should().BeNull();
        utilisation.TotalOperatingDays.Should().BeNull("« 0 jour d'activité » contredisait les 2 058 km");
        utilisation.TotalIdleDays.Should().BeNull("« 31 jours d'inactivité » aussi");
        utilisation.AverageDailyDistanceKm.Should().BeNull();
        utilisation.DailyTrend.Should().BeEmpty();

        rapport.DriverPerformance.AveragePerformanceScore.Should().BeNull();
        rapport.Efficiency.IdleTimePercentage.Should().BeNull();
        rapport.Charts.DailyDistanceTrend.Series.Single().Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_boitier_la_distance_par_type_reprend_le_kilometrage_des_releves()
    {
        using var ctx = await ParcGpaAsync();

        var rapport = await RunAsync(ctx, 8);

        var parType = rapport.FleetOverview.ByType.ToDictionary(t => t.Type);
        parType["Berline"].TotalDistanceKm.Should().Be(780);
        parType["Berline"].AvgDistanceKm.Should().Be(780);
        parType["Fourgon"].TotalDistanceKm.Should().Be(1_278);
        parType["SUV"].TotalDistanceKm.Should().BeNull("un seul relevé : non mesuré, pas 0 km");
        parType["SUV"].AvgDistanceKm.Should().BeNull();
    }

    [Fact]
    public async Task Sans_boitier_la_comparaison_au_mois_precedent_reprend_releves_et_litres()
    {
        using var ctx = await ParcGpaAsync();

        var rapport = await RunAsync(ctx, 8);

        var mom = rapport.MonthOverMonth;
        mom.Distance.CurrentValue.Should().Be(2_058, "le kilométrage du tableau du mois");
        mom.Distance.PreviousValue.Should().Be(1_800, "800 + 1 000 km relevés en juillet ; c'était 0 → 0");
        mom.Distance.Trend.Should().Be("increase");
        mom.FuelConsumption.CurrentValue.Should().Be(265.6, "litres achetés en août");
        mom.FuelConsumption.PreviousValue.Should().Be(220);
        mom.Trips.Should().BeNull("les trajets ne sont pas mesurés sans boîtier");
        mom.Utilization.Should().BeNull();
    }

    [Fact]
    public async Task Les_vehicules_actifs_suivent_les_statuts_reellement_stockes()
    {
        using var ctx = await ParcGpaAsync();

        var rapport = await RunAsync(ctx, 8);

        rapport.FleetOverview.ActiveVehicles.Should().Be(2, "available et in_use : c'était 0 faute de statut « Active »");
        rapport.FleetOverview.InactiveVehicles.Should().Be(1);
        rapport.ExecutiveSummary.ActiveVehicles.Should().Be(2);
    }

    [Fact]
    public async Task Un_parc_equipe_garde_ses_indicateurs_gps_mesures()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.GpsDevices.Add(new GpsDevice { Id = 5, DeviceUid = "DEV-5", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 4, Name = "Camion", Plate = "GJ-473-KS", CompanyId = CompanyId, Status = "available", Type = "camion", GpsDeviceId = 5 });
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

        var rapport = await RunAsync(ctx, 8);

        rapport.FleetHasGps.Should().BeTrue();
        rapport.ExecutiveSummary.TotalTrips.Should().NotBeNull();
        rapport.ExecutiveSummary.TotalDrivingHours.Should().Be(0, "mesuré : aucun trajet terminé");
        rapport.Utilization.TotalOperatingDays.Should().Be(5);
        rapport.Utilization.DailyTrend.Should().HaveCount(31);
        rapport.MonthOverMonth.Trips.Should().NotBeNull();
        rapport.MonthOverMonth.Utilization.Should().NotBeNull();
    }

    // ==================== DEF-026 ====================

    /// <summary>
    /// Septembre 2026 :
    ///   - « Logistique 01 » : un plein de 81 L le 09/09 (213 406 km, le précédent
    ///     date d'août) puis une réparation relevée à 213 548 km — 142 km mesurés,
    ///     81 L achetés, 57 L/100 km au tableau ; consommation réelle 6,3 ;
    ///   - « Sobre » : 60 L puis 50 L sur 800 km — 13,75 L/100 km au tableau, mais
    ///     le premier plein a été brûlé avant : 50 L / 800 km de plein à plein ;
    ///   - « Gourmand » : trois pleins de 60 L sur 800 km — 120 L / 800 km = 15 L/100 km ;
    ///   - « Camion » équipé, sans plein : litres ESTIMÉS au taux du type (25 L/100 km).
    /// </summary>
    private static async Task<TestGisDbContext> ParcSeptembreAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.GpsDevices.Add(new GpsDevice { Id = 7, DeviceUid = "DEV-7", CompanyId = CompanyId });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 39, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 50, Name = "Gourmand", Plate = "GX-500-AA", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 51, Name = "Sobre", Plate = "GX-510-AA", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 60, Name = "Camion", Plate = "GJ-473-KS", CompanyId = CompanyId, Status = "available", Type = "camion", GpsDeviceId = 7 });
        ctx.FuelEntries.AddRange(
            Plein(39, 8, 31, 81, 212_128),
            Plein(39, 9, 9, 81, 213_406),
            Plein(50, 9, 2, 60, 10_000), Plein(50, 9, 12, 60, 10_400), Plein(50, 9, 22, 60, 10_800),
            Plein(51, 9, 2, 60, 20_000), Plein(51, 9, 16, 50, 20_800));
        ctx.Repairs.Add(new Repair
        {
            Id = 1, SocieteId = CompanyId, VehicleId = 39, Reference = "REP-1", Description = "Plaquettes",
            RepairDate = Utc(9, 14), TotalCost = 200, MileageAtRepair = 213_548, Status = "completed"
        });
        for (var day = 1; day <= 5; day++)
        {
            ctx.GpsPositions.Add(new GpsPosition
            {
                Id = day, DeviceId = 7, SpeedKph = 60,
                Latitude = 36.8 + day * 0.05, Longitude = 10.1 + day * 0.05,
                RecordedAt = new DateTime(2026, 9, day, 10, 0, 0, DateTimeKind.Utc).AddHours(1)
            });
        }
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Un_seul_plein_dans_le_mois_ne_leve_plus_d_alerte_de_consommation()
    {
        using var ctx = await ParcSeptembreAsync();

        var rapport = await RunAsync(ctx, 9);

        // Le tableau garde sa définition (litres du mois / km du mois)…
        rapport.Vehicles.Single(v => v.VehicleId == 39).ConsumptionPer100Km.Should().Be(57.04);
        // …mais l'alerte ne se bâtit plus sur ce kilométrage partiel.
        rapport.Alerts.Should().NotContain(a => a.Type == "HighFuelConsumption" && a.VehicleId == 39);
    }

    [Fact]
    public async Task L_alerte_de_consommation_se_fonde_sur_la_consommation_de_plein_a_plein()
    {
        using var ctx = await ParcSeptembreAsync();

        var rapport = await RunAsync(ctx, 9);

        var alertes = rapport.Alerts.Where(a => a.Type == "HighFuelConsumption").ToList();
        alertes.Should().ContainSingle("seul « Gourmand » consomme vraiment plus de 12 L/100 km");

        var gourmand = alertes.Single();
        gourmand.VehicleId.Should().Be(50);
        gourmand.Description.Should().Contain("15 L/100km").And.Contain("120 L pour 800 km, de plein à plein");

        rapport.Vehicles.Single(v => v.VehicleId == 51).ConsumptionPer100Km.Should().Be(13.75,
            "au tableau, « Sobre » dépasse 12 : l'ancienne règle levait une alerte");
    }

    [Fact]
    public async Task Des_litres_estimes_au_taux_du_type_ne_levent_pas_d_alerte()
    {
        using var ctx = await ParcSeptembreAsync();

        var rapport = await RunAsync(ctx, 9);

        var camion = rapport.FuelAnalytics.ByVehicle.Single(v => v.VehicleId == 60);
        camion.IsEstimated.Should().BeTrue();
        camion.ConsumptionPer100Km.Should().Be(25, "le taux par défaut d'un camion, pas une mesure");
        rapport.Alerts.Should().NotContain(a => a.Type == "HighFuelConsumption" && a.VehicleId == 60);
    }

    /// <summary>
    /// Garde-fou du correctif : la réserve sur le kilométrage partiel ne vaut que pour
    /// les relevés saisis. Un véhicule équipé mesure sa distance sur tout le mois
    /// (trajets terminés) ; avec des litres réellement achetés au-delà de 12 L/100 km,
    /// l'alerte doit toujours partir.
    /// </summary>
    [Fact]
    public async Task Un_vehicule_equipe_aux_litres_reels_garde_l_alerte_de_consommation()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.GpsDevices.Add(new GpsDevice { Id = 8, DeviceUid = "DEV-8", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 70, Name = "Benne 01", Plate = "GK-700-AA", CompanyId = CompanyId, Status = "available", GpsDeviceId = 8 });
        // 180 L achetés pour 1 200 km roulés : 15 L/100 km, sur le mois comme de plein à plein.
        ctx.FuelEntries.AddRange(
            Plein(70, 9, 2, 60, 50_000), Plein(70, 9, 12, 60, 50_400), Plein(70, 9, 22, 60, 50_800));
        ctx.Trips.AddRange(
            new Trip { CompanyId = CompanyId, VehicleId = 70, StartTime = Utc(9, 3), DistanceKm = 400, DurationMinutes = 300, Status = "completed" },
            new Trip { CompanyId = CompanyId, VehicleId = 70, StartTime = Utc(9, 13), DistanceKm = 400, DurationMinutes = 300, Status = "completed" },
            new Trip { CompanyId = CompanyId, VehicleId = 70, StartTime = Utc(9, 23), DistanceKm = 400, DurationMinutes = 300, Status = "completed" });
        await ctx.SaveChangesAsync();

        var rapport = await RunAsync(ctx, 9);

        rapport.Vehicles.Single(v => v.VehicleId == 70).DistanceSource.Should().Be("gps");
        var carburant = rapport.FuelAnalytics.ByVehicle.Single(v => v.VehicleId == 70);
        carburant.IsEstimated.Should().BeFalse("litres des pleins saisis");
        carburant.ConsumptionPer100Km.Should().Be(15);

        var alerte = rapport.Alerts.Should()
            .ContainSingle(a => a.Type == "HighFuelConsumption" && a.VehicleId == 70).Subject;
        alerte.Title.Should().Be("Consommation élevée");
        alerte.Severity.Should().Be("Warning");
        alerte.Description.Should().Contain("15 L/100km")
            .And.NotContain("de plein à plein", "la distance du boîtier couvre tout le mois");
    }
}
