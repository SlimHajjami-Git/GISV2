using FluentAssertions;
using GisAPI.Application.Features.AccidentClaims.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.AccidentClaims;

public class AccidentClaimQueriesTests
{
    [Fact]
    public async Task GetAccidentClaims_WithNoData_ReturnsEmptyList()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetAccidentClaimsQueryHandler(context);
        var query = new GetAccidentClaimsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccidentClaims_WithData_ReturnsClaims()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Vehicle 1", CompanyId = 1 });
        context.AccidentClaims.AddRange(
            new AccidentClaim { Id = 1, ClaimNumber = "SIN-2026-0001", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "A", Description = "Test", Severity = "minor", Status = "draft", EstimatedDamage = 1000m },
            new AccidentClaim { Id = 2, ClaimNumber = "SIN-2026-0002", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "B", Description = "Test", Severity = "major", Status = "submitted", EstimatedDamage = 5000m }
        );
        await context.SaveChangesAsync();

        var handler = new GetAccidentClaimsQueryHandler(context);
        var query = new GetAccidentClaimsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAccidentClaims_WithStatusFilter_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Vehicle 1", CompanyId = 1 });
        context.AccidentClaims.AddRange(
            new AccidentClaim { Id = 1, ClaimNumber = "SIN-2026-0001", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "A", Description = "Test", Severity = "minor", Status = "draft", EstimatedDamage = 1000m },
            new AccidentClaim { Id = 2, ClaimNumber = "SIN-2026-0002", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "B", Description = "Test", Severity = "major", Status = "approved", EstimatedDamage = 5000m }
        );
        await context.SaveChangesAsync();

        var handler = new GetAccidentClaimsQueryHandler(context);
        var query = new GetAccidentClaimsQuery(Status: "draft");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.First().Status.Should().Be("draft");
    }

    [Fact]
    public async Task GetAccidentClaims_WithSeverityFilter_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Vehicle 1", CompanyId = 1 });
        context.AccidentClaims.AddRange(
            new AccidentClaim { Id = 1, ClaimNumber = "SIN-2026-0001", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "A", Description = "Test", Severity = "minor", Status = "draft", EstimatedDamage = 1000m },
            new AccidentClaim { Id = 2, ClaimNumber = "SIN-2026-0002", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "B", Description = "Test", Severity = "major", Status = "draft", EstimatedDamage = 10000m }
        );
        await context.SaveChangesAsync();

        var handler = new GetAccidentClaimsQueryHandler(context);
        var query = new GetAccidentClaimsQuery(Severity: "major");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.First().Severity.Should().Be("major");
    }

    [Fact]
    public async Task GetAccidentClaimStats_ReturnsCorrectCounts()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.AccidentClaims.AddRange(
            new AccidentClaim { Id = 1, ClaimNumber = "SIN-2026-0001", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "A", Description = "Test", Severity = "minor", Status = "draft", EstimatedDamage = 1000m },
            new AccidentClaim { Id = 2, ClaimNumber = "SIN-2026-0002", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "B", Description = "Test", Severity = "major", Status = "submitted", EstimatedDamage = 5000m },
            new AccidentClaim { Id = 3, ClaimNumber = "SIN-2026-0003", VehicleId = 1, CompanyId = 1, AccidentDate = DateTime.UtcNow.Date, Location = "C", Description = "Test", Severity = "moderate", Status = "approved", EstimatedDamage = 3000m, ApprovedAmount = 2800m }
        );
        await context.SaveChangesAsync();

        var handler = new GetAccidentClaimStatsQueryHandler(context);
        var query = new GetAccidentClaimStatsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalClaims.Should().Be(3);
        result.DraftCount.Should().Be(1);
        result.SubmittedCount.Should().Be(1);
        result.ApprovedCount.Should().Be(1);
        result.TotalEstimatedDamage.Should().Be(9000m);
        result.TotalApprovedAmount.Should().Be(2800m);
    }

    [Fact]
    public async Task GetAccidentClaimById_ReturnsClaimWithDetails()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", Plate = "123 TUN", CompanyId = 1 });
        context.AccidentClaims.Add(new AccidentClaim 
        { 
            Id = 1, 
            ClaimNumber = "SIN-2026-0001", 
            VehicleId = 1, 
            CompanyId = 1, 
            AccidentDate = DateTime.UtcNow.Date,
            AccidentTime = new TimeSpan(14, 30, 0),
            Location = "Test Location", 
            Description = "Test Description", 
            Severity = "minor", 
            Status = "draft", 
            EstimatedDamage = 1000m 
        });
        await context.SaveChangesAsync();

        var handler = new GetAccidentClaimByIdQueryHandler(context);
        var query = new GetAccidentClaimByIdQuery(Id: 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.ClaimNumber.Should().Be("SIN-2026-0001");
        result.VehicleName.Should().Be("Test Vehicle");
        result.AccidentTime.Should().Be("14:30");
    }
}


