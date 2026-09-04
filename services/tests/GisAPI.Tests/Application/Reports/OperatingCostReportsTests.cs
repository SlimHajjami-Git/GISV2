using FluentAssertions;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetRepairFrequencyReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostRanking;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Les quatre rapports de coûts (R1 coût d'exploitation, R2 évolution mensuelle,
/// R3 classement, R4 fréquence des réparations) sur un même jeu de données :
///
///   - véhicule 1 « Opel » SANS boîtier : 3 pleins avec relevé compteur
///     (46 760 → 47 300 → 47 900 = 1 140 km), un entretien, une assurance
///     (« autres »), 3 réparations retenues + 1 annulée ;
///   - véhicule 2 « Camion » AVEC boîtier : 2 trajets terminés (500 km) + 1 en
///     cours (exclu), un plein sans relevé ;
///   - véhicule 3 « Inactif » : rien ;
///   - véhicule 5 « Atelier » (département 7) : un entretien, pas de km ;
///   - véhicule 4 : autre société, invisible.
/// Période analysée : 01/05/2026 → 30/06/2026 inclus.
/// </summary>
public class OperatingCostReportsTests
{
    private const int CompanyId = 1;
    private static readonly DateTime Start = new(2026, 5, 1);
    private static readonly DateTime End = new(2026, 6, 30);

    private static DateTime Utc(int month, int day) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.GpsDevices.Add(new GpsDevice { Id = 1, DeviceUid = "DEV-1", CompanyId = CompanyId });
        ctx.Departments.Add(new Department { Id = 7, Name = "Atelier", CompanyId = CompanyId });
        ctx.Suppliers.Add(new Supplier { Id = 9, Name = "Garage Test", CompanyId = CompanyId });

        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "524 TFGG 75", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Camion", Plate = "1 TU 1", CompanyId = CompanyId, GpsDeviceId = 1 },
            new Vehicle { Id = 3, Name = "Inactif", CompanyId = CompanyId },
            new Vehicle { Id = 4, Name = "Etranger", CompanyId = 2 },
            new Vehicle { Id = 5, Name = "", Brand = "Renault", Model = "Kangoo", CompanyId = CompanyId, DepartmentId = 7 });

        // Véhicule 1 — pleins avec compteur (mai : 540 km, juin : 600 km)
        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 5), Volume = 40, TotalAmount = 60, OdometerKm = 46_760 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(5, 20), Volume = 38, TotalAmount = 55, OdometerKm = 47_300 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(6, 10), Volume = 39, TotalAmount = 58, OdometerKm = 47_900 },
            // Véhicule 2 — plein sans relevé
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Utc(5, 11), Volume = 80, TotalAmount = 100 },
            // Hors période : ignoré
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Utc(7, 2), Volume = 40, TotalAmount = 999, OdometerKm = 48_500 },
            // Autre société : ignoré
            new FuelEntry { VehicleId = 4, CompanyId = 2, InvoiceDate = Utc(5, 11), Volume = 80, TotalAmount = 100 });

        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "maintenance", Amount = 120, Date = Utc(5, 15) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 830, Date = Utc(6, 1) },
            new VehicleCost { VehicleId = 5, CompanyId = CompanyId, Type = "maintenance", Amount = 50, Date = Utc(5, 2) },
            new VehicleCost { VehicleId = 3, CompanyId = CompanyId, Type = "tax", Amount = 77, Date = Utc(4, 30) }); // hors période

        ctx.Repairs.AddRange(
            new Repair { Id = 101, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-1", Description = "Truc inconnu", RepairType = "freinage", RepairDate = Utc(5, 3), TotalCost = 100, Status = "completed" },
            new Repair { Id = 102, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-2", Description = "Crevaison pneu AVG", RepairDate = Utc(6, 5), TotalCost = 25, Status = "completed", MileageAtRepair = 47_800 },
            new Repair { Id = 103, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-3", Description = "Sonde lambda", RepairDate = Utc(6, 22), TotalCost = 220, Status = "completed", SupplierId = 9 },
            new Repair { Id = 104, SocieteId = CompanyId, VehicleId = 1, Reference = "REP-4", Description = "Plaquettes de frein AV", RepairDate = Utc(6, 25), TotalCost = 145, Status = "cancelled" },
            new Repair { Id = 105, SocieteId = 2, VehicleId = 4, Reference = "REP-5", Description = "Ampoule", RepairDate = Utc(6, 1), TotalCost = 30, Status = "completed" });

        ctx.Trips.AddRange(
            new Trip { CompanyId = CompanyId, VehicleId = 2, StartTime = Utc(5, 10), DistanceKm = 300, Status = "completed" },
            new Trip { CompanyId = CompanyId, VehicleId = 2, StartTime = Utc(6, 3), DistanceKm = 200, Status = "completed" },
            new Trip { CompanyId = CompanyId, VehicleId = 2, StartTime = Utc(6, 4), DistanceKm = 999, Status = "in_progress" });

        await ctx.SaveChangesAsync();
    }

    private static ICurrentTenantService Admin() => TestDbContextFactory.CreateMockTenantService().Object;

    private static ICurrentTenantService RestrictedUser(int userId)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    // ───────────────────────────── R1 ─────────────────────────────

    [Fact]
    public async Task R1_vehicle_without_gps_gets_its_distance_from_fuel_odometer_readings()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Start, End), CancellationToken.None);

        var opel = report.Vehicles.Single(v => v.VehicleId == 1);
        opel.DistanceSource.Should().Be(OperatingCostAggregator.SourceOdometer);
        opel.DistanceKm.Should().Be(1_140m);
        opel.ReliableDistance.Should().BeTrue();
        opel.IgnoredOdometerReadings.Should().Be(0);
        opel.OdometerBreaks.Should().Be(0);
        opel.FuelCost.Should().Be(173m);
        opel.MaintenanceCost.Should().Be(120m);
        opel.RepairCost.Should().Be(345m, "la réparation annulée est exclue");
        opel.OtherCost.Should().Be(830m, "l'assurance est une autre dépense");
        opel.TotalCost.Should().Be(1_468m);
        opel.CostPerKm.Should().Be(1.288m);
        opel.Plate.Should().Be("524 TFGG 75");
    }

    [Fact]
    public async Task R1_vehicle_with_gps_gets_its_distance_from_completed_trips()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Start, End), CancellationToken.None);

        var truck = report.Vehicles.Single(v => v.VehicleId == 2);
        truck.DistanceSource.Should().Be(OperatingCostAggregator.SourceGps);
        truck.DistanceKm.Should().Be(500m, "le trajet en cours n'est pas compté");
        truck.ReliableDistance.Should().BeTrue();
        truck.FuelCost.Should().Be(100m);
        truck.TotalCost.Should().Be(100m);
        truck.CostPerKm.Should().Be(0.2m);
    }

    [Fact]
    public async Task R1_kpis_use_the_weighted_fleet_average_and_rank_by_cost_per_km()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Start, End), CancellationToken.None);

        report.FleetSize.Should().Be(4);
        report.VehicleCount.Should().Be(3, "le véhicule sans aucune activité n'est pas analysé");
        report.VehiclesWithoutDistance.Should().Be(1);
        report.TotalCost.Should().Be(1_618m);
        report.TotalKm.Should().Be(1_640m);
        // (1 468 + 100) / 1 640 — moyenne pondérée, pas la moyenne des €/km
        report.AverageCostPerKm.Should().Be(0.956m);
        report.TotalFuelCost.Should().Be(273m);
        report.TotalMaintenanceCost.Should().Be(170m);
        report.TotalRepairCost.Should().Be(345m);
        report.TotalOtherCost.Should().Be(830m);
        report.DistanceNote.Should().NotBeNullOrWhiteSpace();

        report.Vehicles.Select(v => v.VehicleId).Should().Equal(1, 2, 5);
        report.Vehicles.Select(v => v.Rank).Should().Equal(1, 2, 3);

        report.Vehicles[0].DeviationFromAveragePct.Should().Be(34.7m);
        report.Vehicles[1].DeviationFromAveragePct.Should().Be(-79.1m);

        var atelier = report.Vehicles[2];
        atelier.VehicleName.Should().Be("Renault Kangoo", "nom vide → marque + modèle");
        atelier.DepartmentName.Should().Be("Atelier");
        atelier.DistanceSource.Should().Be(OperatingCostAggregator.SourceNone);
        atelier.DistanceKm.Should().BeNull();
        atelier.CostPerKm.Should().BeNull();
        atelier.DeviationFromAveragePct.Should().BeNull();
    }

    [Fact]
    public async Task R1_filters_by_vehicle_and_by_department()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var handler = new GetOperatingCostReportQueryHandler(ctx, Admin());

        var byVehicle = await handler.Handle(new GetOperatingCostReportQuery(Start, End, VehicleId: 2), CancellationToken.None);
        byVehicle.FleetSize.Should().Be(1);
        byVehicle.Vehicles.Should().ContainSingle(v => v.VehicleId == 2);
        byVehicle.TotalCost.Should().Be(100m);
        byVehicle.AverageCostPerKm.Should().Be(0.2m);

        var byDepartment = await handler.Handle(new GetOperatingCostReportQuery(Start, End, DepartmentId: 7), CancellationToken.None);
        byDepartment.FleetSize.Should().Be(1);
        byDepartment.Vehicles.Should().ContainSingle(v => v.VehicleId == 5);
        byDepartment.AverageCostPerKm.Should().BeNull("aucune distance mesurable");
    }

    [Fact]
    public async Task R1_restricted_user_only_sees_assigned_vehicles()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        ctx.UserVehicles.Add(new UserVehicle { UserId = 42, VehicleId = 2 });
        await ctx.SaveChangesAsync();

        var report = await new GetOperatingCostReportQueryHandler(ctx, RestrictedUser(42))
            .Handle(new GetOperatingCostReportQuery(Start, End), CancellationToken.None);

        report.FleetSize.Should().Be(1);
        report.Vehicles.Should().ContainSingle(v => v.VehicleId == 2);
        report.TotalCost.Should().Be(100m);
    }

    [Fact]
    public async Task R1_empty_company_returns_an_empty_report()
    {
        using var ctx = TestDbContextFactory.Create();

        var report = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Start, End), CancellationToken.None);

        report.FleetSize.Should().Be(0);
        report.Vehicles.Should().BeEmpty();
        report.TotalCost.Should().Be(0m);
        report.AverageCostPerKm.Should().BeNull();
    }

    // ───────────────────────────── R2 ─────────────────────────────

    [Fact]
    public async Task R2_returns_one_bucket_per_month_with_empty_months_at_zero()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetVehicleCostEvolutionQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostEvolutionQuery(1, new DateTime(2026, 4, 1), End), CancellationToken.None);

        report.VehicleId.Should().Be(1);
        report.VehicleName.Should().Be("Opel");
        report.Months.Should().HaveCount(3);
        report.Months.Select(m => (m.Year, m.Month)).Should().Equal((2026, 4), (2026, 5), (2026, 6));

        var april = report.Months[0];
        april.TotalCost.Should().Be(0m);
        april.FuelCost.Should().Be(0m);
        april.DistanceKm.Should().BeNull();
        april.VariationPct.Should().BeNull("premier mois");
        april.MonthName.Should().StartWith("Avr").And.EndWith("2026");

        var may = report.Months[1];
        may.MonthName.Should().Be("Mai 2026");
        may.FuelCost.Should().Be(115m);
        may.MaintenanceCost.Should().Be(120m);
        may.RepairCost.Should().Be(100m);
        may.OtherCost.Should().Be(0m);
        may.TotalCost.Should().Be(335m);
        may.DistanceKm.Should().Be(540m);
        may.VariationPct.Should().BeNull("le mois précédent est à 0");

        var june = report.Months[2];
        june.MonthName.Should().Be("Juin 2026");
        june.FuelCost.Should().Be(58m);
        june.RepairCost.Should().Be(245m);
        june.OtherCost.Should().Be(830m);
        june.TotalCost.Should().Be(1_133m);
        june.DistanceKm.Should().Be(600m);
        june.VariationPct.Should().Be(238.2m);

        report.TotalCost.Should().Be(1_468m);
        report.AverageMonthlyCost.Should().Be(489.33m, "1 468 / 3 mois, mois vide inclus");
        report.HighestMonth!.Month.Should().Be(6);
        report.LowestMonth!.Month.Should().Be(5, "avril est à 0 donc hors classement");
        report.TotalFuelCost.Should().Be(173m);
        report.TotalMaintenanceCost.Should().Be(120m);
        report.TotalRepairCost.Should().Be(345m);
        report.TotalOtherCost.Should().Be(830m);
        report.TotalDistanceKm.Should().Be(1_140m);
        report.DistanceSource.Should().Be(OperatingCostAggregator.SourceOdometer);
    }

    [Fact]
    public async Task R2_vehicle_of_another_company_is_not_found()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var act = () => new GetVehicleCostEvolutionQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostEvolutionQuery(4, Start, End), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task R2_vehicle_without_any_cost_has_no_highest_nor_lowest_month()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetVehicleCostEvolutionQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostEvolutionQuery(3, Start, End), CancellationToken.None);

        report.Months.Should().HaveCount(2);
        report.TotalCost.Should().Be(0m);
        report.AverageMonthlyCost.Should().Be(0m);
        report.HighestMonth.Should().BeNull();
        report.LowestMonth.Should().BeNull();
        report.DistanceSource.Should().Be(OperatingCostAggregator.SourceNone);
    }

    // ───────────────────────────── R3 ─────────────────────────────

    [Fact]
    public async Task R3_truncates_the_list_to_top_n_but_keeps_fleet_wide_kpis()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetVehicleCostRankingQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostRankingQuery(Start, End, Top: 1), CancellationToken.None);

        report.Vehicles.Should().ContainSingle();
        report.Vehicles[0].VehicleId.Should().Be(1);
        report.Vehicles[0].Rank.Should().Be(1);

        // KPI sur TOUT le parc, pas sur le top 1
        report.VehicleCount.Should().Be(3);
        report.FleetSize.Should().Be(4);
        report.TotalCost.Should().Be(1_618m);
        report.TotalKm.Should().Be(1_640m);
        report.AverageCostPerKm.Should().Be(0.956m);
    }

    [Fact]
    public async Task R3_department_filter_is_honoured()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetVehicleCostRankingQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostRankingQuery(Start, End, Top: 10, DepartmentId: 7), CancellationToken.None);

        report.FleetSize.Should().Be(1);
        report.Vehicles.Should().ContainSingle(v => v.VehicleId == 5);
    }

    // ───────────────────────────── R4 ─────────────────────────────

    [Fact]
    public async Task R4_counts_interventions_excluding_cancelled_and_builds_the_synthesis()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetRepairFrequencyReportQueryHandler(ctx, Admin())
            .Handle(new GetRepairFrequencyReportQuery(Start, End), CancellationToken.None);

        report.FleetSize.Should().Be(4);
        report.TotalInterventions.Should().Be(3, "la réparation annulée est exclue");
        report.VehiclesConcerned.Should().Be(1);
        report.AverageInterventionsPerVehicle.Should().Be(0.75m, "3 interventions / 4 véhicules du parc");
        report.AverageFrequencyPer1000Km.Should().Be(1.83m, "3 / 1 640 km mesurables × 1000");
        report.TotalRepairCost.Should().Be(345m);
        report.AverageCostPerIntervention.Should().Be(115m);
        report.VehiclesAboveAverage.Should().Be(1);
        report.VehiclesBelowAverage.Should().Be(3);

        report.Vehicles.Should().HaveCount(4, "tout le périmètre, 0 intervention inclus");
        report.Vehicles[0].VehicleId.Should().Be(1);
        report.Vehicles[0].Rank.Should().Be(1);
        report.Vehicles[0].Interventions.Should().Be(3);
        report.Vehicles[0].DistanceKm.Should().Be(1_140m);
        report.Vehicles[0].FrequencyPer1000Km.Should().Be(2.63m);
        report.Vehicles[0].AverageCostPerIntervention.Should().Be(115m);
        report.Vehicles[0].DeviationFromAveragePct.Should().Be(300m);
        report.Vehicles.Skip(1).Should().OnlyContain(v => v.Interventions == 0 && v.AverageCostPerIntervention == null);
        report.Vehicles.Single(v => v.VehicleId == 2).FrequencyPer1000Km.Should().Be(0m, "km mesurés mais aucune intervention");
        report.Vehicles.Single(v => v.VehicleId == 3).FrequencyPer1000Km.Should().BeNull("aucun km");
        report.Vehicles.Single(v => v.VehicleId == 2).DeviationFromAveragePct.Should().Be(-100m);

        report.MostFrequentVehicle!.VehicleId.Should().Be(1);
        report.LeastFrequentVehicle!.VehicleId.Should().Be(1, "seul véhicule concerné");
    }

    [Fact]
    public async Task R4_by_type_uses_the_explicit_type_or_infers_it_from_the_description()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetRepairFrequencyReportQueryHandler(ctx, Admin())
            .Handle(new GetRepairFrequencyReportQuery(Start, End), CancellationToken.None);

        report.ByType.Should().HaveCount(3);
        report.ByType.Sum(t => t.Count).Should().Be(3);
        report.ByType.Select(t => t.Type).Should().Equal("electrique", "freinage", "pneumatique");
        report.ByType.Should().OnlyContain(t => t.Pct == 33.3m);
        report.ByType.Single(t => t.Type == "electrique").Label.Should().Be("Électrique");
        report.ByType.Single(t => t.Type == "electrique").TotalCost.Should().Be(220m);
    }

    [Fact]
    public async Task R4_details_the_most_frequent_vehicle_interventions_newest_first_with_supplier_name()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetRepairFrequencyReportQueryHandler(ctx, Admin())
            .Handle(new GetRepairFrequencyReportQuery(Start, End), CancellationToken.None);

        var detail = report.MostFrequentVehicleInterventions;
        detail.Should().HaveCount(3);
        detail.Select(d => d.RepairId).Should().Equal(103, 102, 101);

        detail[0].Type.Should().Be("electrique");
        detail[0].TypeLabel.Should().Be("Électrique");
        detail[0].TypeInferred.Should().BeTrue();
        detail[0].SupplierName.Should().Be("Garage Test");
        detail[0].TotalCost.Should().Be(220m);
        detail[0].Reference.Should().Be("REP-3");
        detail[0].Status.Should().Be("completed");

        detail[1].MileageAtRepair.Should().Be(47_800);
        detail[1].SupplierName.Should().BeNull();

        detail[2].Type.Should().Be("freinage");
        detail[2].TypeInferred.Should().BeFalse("type saisi explicitement");
    }

    [Fact]
    public async Task R4_with_no_repair_has_no_most_frequent_vehicle_and_null_averages()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var report = await new GetRepairFrequencyReportQueryHandler(ctx, Admin())
            .Handle(new GetRepairFrequencyReportQuery(Start, End, VehicleId: 2), CancellationToken.None);

        report.FleetSize.Should().Be(1);
        report.TotalInterventions.Should().Be(0);
        report.VehiclesConcerned.Should().Be(0);
        report.AverageInterventionsPerVehicle.Should().Be(0m);
        report.AverageCostPerIntervention.Should().BeNull();
        report.AverageFrequencyPer1000Km.Should().Be(0m, "500 km mesurés, 0 intervention");
        report.MostFrequentVehicle.Should().BeNull();
        report.LeastFrequentVehicle.Should().BeNull();
        report.ByType.Should().BeEmpty();
        report.MostFrequentVehicleInterventions.Should().BeEmpty();
        report.Vehicles.Should().ContainSingle().Which.DeviationFromAveragePct.Should().BeNull("moyenne = 0");
    }
}
