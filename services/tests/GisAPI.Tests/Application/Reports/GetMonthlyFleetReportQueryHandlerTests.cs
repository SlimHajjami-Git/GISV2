using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

public class GetMonthlyFleetReportQueryHandlerTests
{
    [Fact]
    public async Task Handle_WithNoVehicles_ReturnsEmptyReport()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var device = new GpsDevice { Id = 1, Uid = "TEST001", CompanyId = 1 };
        context.GpsDevices.Add(device);

        var vehicles = new List<Vehicle>
        {
            new() { Id = 1, Name = "Vehicle 1", CompanyId = 1, Status = "Active", Type = "Car", GpsDeviceId = 1 },
            new() { Id = 2, Name = "Vehicle 2", CompanyId = 1, Status = "Active", Type = "Truck" },
            new() { Id = 3, Name = "Vehicle 3", CompanyId = 1, Status = "Inactive", Type = "Car" }
        };
        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var device = new GpsDevice { Id = 1, Uid = "TEST001", CompanyId = 1 };
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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

        var handler = new GetMonthlyFleetReportQueryHandler(context);
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
        var handler = new GetMonthlyFleetReportQueryHandler(context);
        var query = new GetMonthlyFleetReportQuery(2024, 6); // June

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.MonthName.Should().Contain("juin");
        result.MonthName.Should().Contain("2024");
    }
}
