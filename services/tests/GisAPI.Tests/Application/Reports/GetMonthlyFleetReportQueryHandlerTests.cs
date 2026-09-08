using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

public class GetMonthlyFleetReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithNoVehicles_ReturnsEmptyReport()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Year.Should().Be(2024);
        result.Month.Should().Be(1);
        result.FleetOverview.TotalVehicles.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithVehicles_ReturnsFleetOverview()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);

        var device = new GpsDevice { Id = 1, DeviceUid = "TEST001", CompanyId = 1 };
        context.GpsDevices.Add(device);

        var vehicles = new List<Vehicle>
        {
            new() { Id = 1, Name = "Vehicle 1", CompanyId = 1, Status = "Active", Type = "Car", GpsDeviceId = 1 },
            new() { Id = 2, Name = "Vehicle 2", CompanyId = 1, Status = "Active", Type = "Truck" },
            new() { Id = 3, Name = "Vehicle 3", CompanyId = 1, Status = "Inactive", Type = "Car" }
        };
        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.FleetOverview.TotalVehicles.Should().Be(3);
        result.FleetOverview.ActiveVehicles.Should().Be(2);
        result.FleetOverview.InactiveVehicles.Should().Be(1);
        result.FleetOverview.ByType.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WithPositions_CalculatesUtilization()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);

        var device = new GpsDevice { Id = 1, DeviceUid = "TEST001", CompanyId = 1 };
        context.GpsDevices.Add(device);

        var vehicle = new Vehicle 
        { 
            Id = 1, 
            Name = "Vehicle 1", 
            CompanyId = 1, 
            Status = "Active", 
            Type = "Car",
            GpsDeviceId = 1 
        };
        context.Vehicles.Add(vehicle);

        // Add positions for January 2024
        var positions = new List<GpsPosition>();
        for (int day = 1; day <= 10; day++)
        {
            positions.Add(new GpsPosition
            {
                Id = day,
                DeviceId = 1,
                Latitude = 36.8 + (day * 0.01),
                Longitude = 10.1 + (day * 0.01),
                SpeedKph = 50,
                RecordedAt = new DateTime(2024, 1, day, 10, 0, 0).AddHours(1) // UTC+1
            });
        }
        context.GpsPositions.AddRange(positions);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Utilization.ByVehicle.Should().HaveCount(1);
        result.Utilization.ByVehicle[0].OperatingDays.Should().Be(10);
        result.Utilization.TotalOperatingDays.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Handle_GeneratesExecutiveSummary()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);

        var vehicle = new Vehicle 
        { 
            Id = 1, 
            Name = "Vehicle 1", 
            CompanyId = 1, 
            Status = "Active"
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.ExecutiveSummary.Should().NotBeNull();
        result.ExecutiveSummary.TotalVehicles.Should().Be(1);
        result.ExecutiveSummary.KeyInsights.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_GeneratesKPIs()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.KeyPerformanceIndicators.Should().NotBeEmpty();
        result.KeyPerformanceIndicators.Should().Contain(k => k.Name == "Taux d'utilisation flotte");
        result.KeyPerformanceIndicators.Should().Contain(k => k.Name == "Consommation moyenne");
    }

    [Fact]
    public async Task Handle_GeneratesChartData()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);

        var vehicles = new List<Vehicle>
        {
            new() { Id = 1, Name = "Vehicle 1", CompanyId = 1, Status = "Active", Type = "Car" },
            new() { Id = 2, Name = "Vehicle 2", CompanyId = 1, Status = "Active", Type = "Truck" }
        };
        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Charts.Should().NotBeNull();
        result.Charts.FleetComposition.Should().NotBeNull();
        result.Charts.FleetComposition.Labels.Should().HaveCountGreaterThan(0);
        result.Charts.CostDistribution.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_CalculatesMonthOverMonthComparison()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 2); // February

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.MonthOverMonth.Should().NotBeNull();
        result.MonthOverMonth.ComparisonPeriod.Should().Be("Mois précédent");
        result.MonthOverMonth.Distance.Should().NotBeNull();
        result.MonthOverMonth.FuelConsumption.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_WithVehicleFilter_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        
        var company = new Company { Id = 1, Name = "Test Company" };
        context.Companies.Add(company);

        var vehicles = new List<Vehicle>
        {
            new() { Id = 1, Name = "Vehicle 1", CompanyId = 1, Status = "Active" },
            new() { Id = 2, Name = "Vehicle 2", CompanyId = 1, Status = "Active" },
            new() { Id = 3, Name = "Vehicle 3", CompanyId = 1, Status = "Active" }
        };
        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 1, VehicleIds: new[] { 1, 2 });

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.FleetOverview.TotalVehicles.Should().Be(2);
    }

    [Fact]
    public async Task Handle_GeneratesCorrectMonthName()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetMonthlyFleetReportQueryHandler(context, TestDbContextFactory.CreateMockTenantService().Object);
        var query = new GetMonthlyFleetReportQuery(2024, 6); // June

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.MonthName.Should().Contain("juin");
        result.MonthName.Should().Contain("2024");
    }

    // ==================== COÛTS RÉELS ====================
    //
    // Le rapport n'ouvrait AUCUNE table de dépense : le carburant valait
    // « litres estimés × 2,1 », l'assurance « nombre de véhicules × 150 », les
    // autres coûts « nombre de véhicules × 50 » et la maintenance quatre lignes
    // Vidange/Pneus/Freins/Révision calculées sur le nombre de véhicules. Les
    // cas suivants figent la lecture des dépenses réellement saisies.

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Janvier 2024, société 1 :
    ///   - véhicule 1 « Opel » SANS boîtier : 2 pleins (460 au total, 70 L,
    ///     compteur 10 000 → 10 600 = 600 km), un entretien 120, une assurance
    ///     830, une visite technique 60, une réparation 300 et une annulée 145 ;
    ///   - véhicule 2 « Camion » AVEC boîtier : 500 km de trajets terminés et
    ///     une dépense de type carburant 100 (50 L), aucune position ;
    ///   - une vignette 200 en décembre 2023 (mois de comparaison) ;
    ///   - un plein en février 2024 et une assurance d'une autre société : ignorés.
    /// Total attendu sur janvier : 560 carburant + 120 entretien + 300 réparation
    /// + 830 assurance + 60 visite = 1 870.
    /// </summary>
    private static async Task SeedExpensesAsync(TestGisDbContext ctx)
    {
        ctx.Companies.Add(new Company { Id = 1, Name = "Test Company" });
        ctx.Companies.Add(new Company { Id = 2, Name = "Autre Société" });
        ctx.GpsDevices.Add(new GpsDevice { Id = 1, DeviceUid = "TEST001", CompanyId = 1 });

        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "1 TU 1", CompanyId = 1, Status = "Active", Type = "berline" },
            new Vehicle { Id = 2, Name = "Camion", Plate = "2 TU 2", CompanyId = 1, Status = "Active", Type = "camion", GpsDeviceId = 1 },
            new Vehicle { Id = 4, Name = "Etranger", CompanyId = 2, Status = "Active" });

        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = 1, InvoiceDate = Utc(2024, 1, 5), Volume = 40, TotalAmount = 260, OdometerKm = 10_000 },
            new FuelEntry { VehicleId = 1, CompanyId = 1, InvoiceDate = Utc(2024, 1, 25), Volume = 30, TotalAmount = 200, OdometerKm = 10_600 },
            // Hors période : ignoré
            new FuelEntry { VehicleId = 1, CompanyId = 1, InvoiceDate = Utc(2024, 2, 3), Volume = 45, TotalAmount = 999, OdometerKm = 11_200 });

        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = 1, Type = "maintenance", Amount = 120, Date = Utc(2024, 1, 12) },
            new VehicleCost { VehicleId = 1, CompanyId = 1, Type = "insurance", Amount = 830, Date = Utc(2024, 1, 15) },
            new VehicleCost { VehicleId = 1, CompanyId = 1, Type = "technical_inspection", Amount = 60, Date = Utc(2024, 1, 20) },
            new VehicleCost { VehicleId = 2, CompanyId = 1, Type = "fuel", Amount = 100, Liters = 50, Date = Utc(2024, 1, 18) },
            // Mois précédent (comparaison) et autre société : hors du total de janvier
            new VehicleCost { VehicleId = 1, CompanyId = 1, Type = "tax", Amount = 200, Date = Utc(2023, 12, 15) },
            new VehicleCost { VehicleId = 4, CompanyId = 2, Type = "insurance", Amount = 999, Date = Utc(2024, 1, 15) });

        ctx.Repairs.AddRange(
            new Repair { Id = 101, SocieteId = 1, VehicleId = 1, Reference = "REP-1", Description = "Plaquettes de frein AV", RepairDate = Utc(2024, 1, 9), TotalCost = 300, Status = "completed" },
            new Repair { Id = 102, SocieteId = 1, VehicleId = 1, Reference = "REP-2", Description = "Pare-brise", RepairDate = Utc(2024, 1, 11), TotalCost = 145, Status = "cancelled" },
            new Repair { Id = 103, SocieteId = 2, VehicleId = 4, Reference = "REP-3", Description = "Ampoule", RepairDate = Utc(2024, 1, 9), TotalCost = 70, Status = "completed" });

        ctx.Trips.AddRange(
            new Trip { CompanyId = 1, VehicleId = 2, StartTime = Utc(2024, 1, 8), DistanceKm = 300, DurationMinutes = 240, Status = "completed" },
            new Trip { CompanyId = 1, VehicleId = 2, StartTime = Utc(2024, 1, 22), DistanceKm = 200, DurationMinutes = 180, Status = "completed" },
            new Trip { CompanyId = 1, VehicleId = 2, StartTime = Utc(2024, 1, 23), DistanceKm = 999, DurationMinutes = 600, Status = "in_progress" });

        await ctx.SaveChangesAsync();
    }

    private static Task<MonthlyFleetReportDto> RunAsync(TestGisDbContext ctx, int companyId = 1, int year = 2024, int month = 1) =>
        RunAsync(ctx, TestDbContextFactory.CreateMockTenantService(companyId).Object, year, month);

    private static Task<MonthlyFleetReportDto> RunAsync(
        TestGisDbContext ctx, ICurrentTenantService tenant, int year = 2024, int month = 1) =>
        new GetMonthlyFleetReportQueryHandler(ctx, tenant)
            .Handle(new GetMonthlyFleetReportQuery(year, month), CancellationToken.None);

    /// <summary>
    /// Utilisateur NON administrateur : <see cref="VehicleScope"/> le borne aux
    /// véhicules de la table UserVehicles (aucun ⇒ rapport vide).
    /// </summary>
    private static ICurrentTenantService RestrictedUser(int userId, int companyId = 1)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(companyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserEmail).Returns("employe@test.com");
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    [Fact]
    public async Task Handle_CostAnalysis_ReadsRealExpenses_NotPerVehicleConstants()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        var costs = report.CostAnalysis;
        costs.FuelCost.Should().Be(560m, "460 de pleins saisis + 100 de dépense de type carburant");
        costs.MaintenanceCost.Should().Be(420m, "120 d'entretien + 300 de réparation retenue (l'annulée est exclue)");
        costs.InsuranceCost.Should().Be(830m, "le montant réellement saisi, pas 2 véhicules × 150");
        costs.OtherCosts.Should().Be(60m, "la visite technique, pas 2 véhicules × 50");
        costs.TotalOperationalCost.Should().Be(1_870m);
        (costs.FuelCost + costs.MaintenanceCost + costs.InsuranceCost + costs.OtherCosts)
            .Should().Be(costs.TotalOperationalCost, "aucune dépense ne doit disparaître du total");
        report.ExecutiveSummary.TotalOperationalCost.Should().Be(1_870m);
    }

    [Fact]
    public async Task Handle_CostAnalysis_KeepsRepairsAsTheirOwnCategory()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        var byCategory = report.CostAnalysis.ByCategory;
        byCategory.Select(c => c.Category).Should().Equal(
            "Carburant", "Entretien", "Réparations", "Assurance", "Visite technique");
        byCategory.Single(c => c.Category == "Réparations").Amount.Should().Be(300m);
        byCategory.Single(c => c.Category == "Assurance").Amount.Should().Be(830m);
        byCategory.Sum(c => c.Amount).Should().Be(report.CostAnalysis.TotalOperationalCost);
        byCategory.Single(c => c.Category == "Assurance").Percentage.Should().Be(44.4);
    }

    [Fact]
    public async Task Handle_CostAnalysis_RatesCostsOnMeasuredDistance()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        // 600 km au compteur (véhicule sans boîtier) + 500 km de trajets terminés
        report.CostAnalysis.CostPerKm.Should().Be(1.7m);
        report.CostAnalysis.CostPerVehicle.Should().Be(935m);

        var opel = report.CostAnalysis.ByVehicle.Single(v => v.VehicleId == 1);
        opel.TotalCost.Should().Be(1_770m);
        opel.FuelCost.Should().Be(460m);
        opel.MaintenanceCost.Should().Be(420m);
        opel.CostPerKm.Should().Be(2.95m);

        var camion = report.CostAnalysis.ByVehicle.Single(v => v.VehicleId == 2);
        camion.TotalCost.Should().Be(100m);
        camion.CostPerKm.Should().Be(0.2m);
    }

    [Fact]
    public async Task Handle_Maintenance_ListsRealInterventions_NotPlaceholderTypes()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        var maintenance = report.Maintenance;
        maintenance.TotalMaintenanceCost.Should().Be(420m);
        maintenance.TotalMaintenanceEvents.Should().Be(2, "un entretien saisi et une réparation retenue");
        maintenance.ByType.Should().NotContain(t => t.Type == "Vidange" || t.Type == "Pneus" || t.Type == "Révision");
        maintenance.ByType.Select(t => t.Type).Should().Equal("Freinage", "Entretien");
        maintenance.ByType.Single(t => t.Type == "Freinage").TotalCost.Should().Be(300m);
        maintenance.ByType.Single(t => t.Type == "Entretien").Count.Should().Be(1);
        maintenance.ByType.Sum(t => t.TotalCost).Should().Be(maintenance.TotalMaintenanceCost);
        maintenance.ScheduledMaintenances.Should().Be(0, "rien en base ne distingue planifié et subi");
        maintenance.UnscheduledMaintenances.Should().Be(0);
        maintenance.ByVehicle.Single().VehicleId.Should().Be(1);
        maintenance.RecentEvents.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_FuelAnalytics_PrefersRealFillsOverEstimates()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        var fuel = report.FuelAnalytics;
        fuel.TotalFuelCost.Should().Be(560m, "le coût vient des saisies, jamais de litres × 2,1");
        fuel.TotalFuelConsumedLiters.Should().Be(120, "70 L de pleins + 50 L de dépense carburant");
        fuel.IsEstimated.Should().BeFalse();
        fuel.ByVehicle.Should().HaveCount(2);
        fuel.ByVehicle.Should().OnlyContain(v => !v.IsEstimated);
        fuel.ByVehicle.Single(v => v.VehicleId == 1).TotalDistanceKm.Should().Be(600, "relevés compteur des pleins");
        fuel.ByVehicle.Single(v => v.VehicleId == 2).ConsumptionPer100Km.Should().Be(10);
    }

    [Fact]
    public async Task Handle_WithoutAnyExpense_ReportsZeroCost_AndNoInventedBreakdown()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Companies.Add(new Company { Id = 1, Name = "Test Company" });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "V1", CompanyId = 1, Status = "Active" },
            new Vehicle { Id = 2, Name = "V2", CompanyId = 1, Status = "Active" },
            new Vehicle { Id = 3, Name = "V3", CompanyId = 1, Status = "Active" });
        await ctx.SaveChangesAsync();

        var report = await RunAsync(ctx);

        report.CostAnalysis.TotalOperationalCost.Should().Be(0m, "aucune dépense saisie");
        report.CostAnalysis.InsuranceCost.Should().Be(0m, "avant : 3 véhicules × 150");
        report.CostAnalysis.OtherCosts.Should().Be(0m, "avant : 3 véhicules × 50");
        report.CostAnalysis.ByCategory.Should().BeEmpty();
        report.CostAnalysis.ByVehicle.Should().BeEmpty();
        report.Maintenance.TotalMaintenanceCost.Should().Be(0m);
        report.Maintenance.ByType.Should().BeEmpty();
        report.FuelAnalytics.TotalFuelCost.Should().Be(0m);
        report.Charts.CostDistribution.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CostAnalysis_StaysInsideTheTenant()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var other = await RunAsync(ctx, companyId: 2);

        other.FleetOverview.TotalVehicles.Should().Be(1);
        other.CostAnalysis.TotalOperationalCost.Should().Be(1_069m, "999 d'assurance + 70 de réparation, rien de la société 1");
        other.CostAnalysis.InsuranceCost.Should().Be(999m);
    }

    [Fact]
    public async Task Handle_MonthOverMonth_ComparesRealCosts()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        report.MonthOverMonth.Cost.CurrentValue.Should().Be(1_870);
        report.MonthOverMonth.Cost.PreviousValue.Should().Be(200, "la vignette de décembre 2023");
    }

    // ==================== INDICATEURS MESURÉS ====================

    [Fact]
    public async Task Handle_Efficiency_DropsTheThreeHardcodedRates()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        // 95 / 92 / 88 n'avaient aucune source en base ; leur moyenne — le
        // « score d'efficacité » — valait donc toujours 91,7.
        report.Efficiency.Metrics.Select(m => m.Name).Should().Equal("Temps d'inactivité");
        report.KeyPerformanceIndicators.Should().NotContain(k => k.Name == "Efficacité opérationnelle");
        report.KeyPerformanceIndicators.Should().NotContain(k => k.Value == 91.7);
    }

    [Fact]
    public async Task Handle_DrivingHours_ComeFromCompletedTrips_NotEightHoursPerActiveDay()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        var report = await RunAsync(ctx);

        // 240 + 180 minutes de trajets terminés = 7 h. Le trajet en cours
        // (600 min) est exclu, comme pour la distance.
        report.ExecutiveSummary.TotalDrivingHours.Should().Be(7);
    }

    [Fact]
    public async Task Handle_DrivingHours_AreZeroWithoutAnyCompletedTrip()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Companies.Add(new Company { Id = 1, Name = "Test Company" });
        ctx.GpsDevices.Add(new GpsDevice { Id = 1, DeviceUid = "TEST001", CompanyId = 1 });
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "V1", CompanyId = 1, Status = "Active", GpsDeviceId = 1 });
        for (int day = 1; day <= 10; day++)
        {
            ctx.GpsPositions.Add(new GpsPosition
            {
                Id = day,
                DeviceId = 1,
                Latitude = 36.8 + (day * 0.01),
                Longitude = 10.1 + (day * 0.01),
                SpeedKph = 50,
                RecordedAt = new DateTime(2024, 1, day, 10, 0, 0).AddHours(1)
            });
        }
        await ctx.SaveChangesAsync();

        var report = await RunAsync(ctx);

        report.Utilization.ByVehicle.Single().OperatingDays.Should().Be(10);
        report.ExecutiveSummary.TotalDrivingHours.Should().Be(0, "avant : 10 jours actifs × 8 = 80 heures");
    }

    // ==================== PORTÉE VÉHICULES ====================

    /// <summary>
    /// Employé affecté au SEUL véhicule 1 (l'Opel sans boîtier), le Camion et ses
    /// 500 km de trajets restant hors de sa portée. Sans le filtre de portée dans
    /// GetVehiclesAsync, l'agrégateur bornait bien les COÛTS à l'Opel pendant que
    /// la distance, la consommation et les listes couvraient les deux véhicules :
    /// numérateur restreint, dénominateur du parc entier.
    /// </summary>
    [Fact]
    public async Task Handle_RestrictedUser_ScopesTotalsAndRatiosToAssignedVehicles()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);
        ctx.UserVehicles.Add(new UserVehicle { UserId = 7, VehicleId = 1 });
        // Positions du Camion : hors portée, elles gonflaient le dénominateur.
        for (int day = 1; day <= 10; day++)
        {
            ctx.GpsPositions.Add(new GpsPosition
            {
                Id = day,
                DeviceId = 1,
                Latitude = 36.8 + (day * 0.05),
                Longitude = 10.1 + (day * 0.05),
                SpeedKph = 60,
                RecordedAt = new DateTime(2024, 1, day, 10, 0, 0).AddHours(1)
            });
        }
        await ctx.SaveChangesAsync();

        var report = await RunAsync(ctx, RestrictedUser(7));

        // Le parc du rapport se limite au véhicule affecté…
        report.FleetOverview.TotalVehicles.Should().Be(1);
        report.FleetOverview.ByType.Select(t => t.Type).Should().Equal("berline");
        report.Utilization.ByVehicle.Should().BeEmpty("l'Opel n'a pas de boîtier, et le Camion est hors portée");

        // …et rien du Camion ne transparaît (plaque, nom, utilisation).
        report.FuelAnalytics.ByVehicle.Should().ContainSingle().Which.VehicleId.Should().Be(1);
        report.CostAnalysis.ByVehicle.Should().ContainSingle().Which.VehicleId.Should().Be(1);
        report.Maintenance.ByVehicle.Should().OnlyContain(v => v.VehicleId == 1);

        // Totaux : les dépenses de l'Opel seul.
        report.CostAnalysis.FuelCost.Should().Be(460m);
        report.CostAnalysis.MaintenanceCost.Should().Be(420m);
        report.CostAnalysis.InsuranceCost.Should().Be(830m);
        report.CostAnalysis.OtherCosts.Should().Be(60m);
        report.CostAnalysis.TotalOperationalCost.Should().Be(1_770m);

        // Ratios : numérateur ET dénominateur sur le même périmètre. Les 600 km
        // du compteur de l'Opel, sans les 500 km de trajets du Camion ni la
        // distance de ses positions.
        report.CostAnalysis.CostPerKm.Should().Be(2.95m, "1 770 / 600 km, pas 1 770 / (600 + Camion)");
        report.CostAnalysis.CostPerVehicle.Should().Be(1_770m);
        report.FuelAnalytics.TotalFuelConsumedLiters.Should().Be(70, "les 50 L du Camion sont hors portée");
        report.FuelAnalytics.AverageConsumptionPer100Km.Should().Be(11.67, "70 L / 600 km");
        report.FuelAnalytics.ByVehicle.Single().ConsumptionPer100Km.Should().Be(11.67);
        report.FuelAnalytics.IsEstimated.Should().BeFalse("le Camion entrait sans litres et faussait le drapeau");

        // Heures de conduite : les trajets du Camion ne comptent pas.
        report.ExecutiveSummary.TotalDrivingHours.Should().Be(0);
        report.ExecutiveSummary.TotalOperationalCost.Should().Be(1_770m);
    }

    [Fact]
    public async Task Handle_RestrictedUserWithoutAnyAssignment_ReturnsAnEmptyReport()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedExpensesAsync(ctx);

        // Portée VIDE = aucun véhicule visible, surtout pas « pas de filtre ».
        var report = await RunAsync(ctx, RestrictedUser(9));

        report.FleetOverview.TotalVehicles.Should().Be(0);
        report.FuelAnalytics.ByVehicle.Should().BeEmpty();
        report.CostAnalysis.ByVehicle.Should().BeEmpty();
        report.CostAnalysis.TotalOperationalCost.Should().Be(0m);
        report.Maintenance.TotalMaintenanceEvents.Should().Be(0);
        report.ExecutiveSummary.TotalDrivingHours.Should().Be(0);
        report.MonthOverMonth.Cost.CurrentValue.Should().Be(0);
        report.MonthOverMonth.Cost.PreviousValue.Should().Be(0, "même la vignette de décembre reste hors de portée");
    }
}


