using FluentAssertions;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Documents;

public class DocumentExpiriesTests
{
    [Fact]
    public async Task GetExpiries_WithNoData_ReturnsEmptyList()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetExpiriesQueryHandler(context);
        var query = new GetExpiriesQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExpiries_WithVehicles_ReturnsExpiries()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle 
            { 
                Id = 1, 
                Name = "Vehicle 1", 
                CompanyId = 1, 
                InsuranceExpiry = DateTime.UtcNow.AddDays(10),
                TechnicalInspectionExpiry = DateTime.UtcNow.AddDays(-5) // Expired
            },
            new Vehicle 
            { 
                Id = 2, 
                Name = "Vehicle 2", 
                CompanyId = 1,
                InsuranceExpiry = DateTime.UtcNow.AddDays(60)
            }
        );
        await context.SaveChangesAsync();

        var handler = new GetExpiriesQueryHandler(context);
        var query = new GetExpiriesQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetExpiries_WithStatusFilter_FiltersExpired()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle 
            { 
                Id = 1, 
                Name = "Vehicle 1", 
                CompanyId = 1, 
                InsuranceExpiry = DateTime.UtcNow.AddDays(-10) // Expired
            },
            new Vehicle 
            { 
                Id = 2, 
                Name = "Vehicle 2", 
                CompanyId = 1,
                InsuranceExpiry = DateTime.UtcNow.AddDays(60) // OK
            }
        );
        await context.SaveChangesAsync();

        var handler = new GetExpiriesQueryHandler(context);
        var query = new GetExpiriesQuery(Status: "expired");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().AllSatisfy(e => e.Status.Should().Be("expired"));
    }

    [Fact]
    public async Task GetExpiryStats_ReturnsCorrectCounts()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "V1", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(-5) }, // Expired
            new Vehicle { Id = 2, Name = "V2", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(15) }, // Expiring soon
            new Vehicle { Id = 3, Name = "V3", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(90) }  // OK
        );
        await context.SaveChangesAsync();

        var handler = new GetExpiryStatsQueryHandler(context);
        var query = new GetExpiryStatsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.ExpiredCount.Should().BeGreaterThanOrEqualTo(1);
        result.ExpiringSoonCount.Should().BeGreaterThanOrEqualTo(1);
        result.OkCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task RenewDocument_CreatesVehicleCostAndUpdatesExpiry()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        
        context.Vehicles.Add(new Vehicle 
        { 
            Id = 1, 
            Name = "Test Vehicle", 
            CompanyId = 1,
            InsuranceExpiry = DateTime.UtcNow.AddDays(-10) // Expired
        });
        await context.SaveChangesAsync();

        var handler = new RenewDocumentCommandHandler(context, tenantService.Object);
        var command = new RenewDocumentCommand(
            1,
            "insurance",
            500m,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.AddYears(1).Date,
            "POL-2026-001",
            "STAR Assurances",
            "Full coverage",
            null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        
        // Check VehicleCost created
        var cost = await context.VehicleCosts.FindAsync(result);
        cost.Should().NotBeNull();
        cost!.Type.Should().Be("insurance");
        cost.Amount.Should().Be(500m);
        
        // Check Vehicle expiry updated
        var vehicle = await context.Vehicles.FindAsync(1);
        vehicle!.InsuranceExpiry.Should().BeCloseTo(DateTime.UtcNow.AddYears(1).Date, TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task RenewDocument_WithInvalidType_ThrowsException()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1 });
        await context.SaveChangesAsync();

        var handler = new RenewDocumentCommandHandler(context, tenantService.Object);
        var command = new RenewDocumentCommand(
            1,
            "invalid_type",
            100m,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.AddYears(1).Date,
            null,
            null,
            null,
            null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task GetRenewalHistory_ReturnsHistoryForVehicle()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1 });
        context.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, VehicleId = 1, Type = "insurance", Amount = 500m, Date = DateTime.UtcNow.AddMonths(-6), CompanyId = 1 },
            new VehicleCost { Id = 2, VehicleId = 1, Type = "technical_inspection", Amount = 100m, Date = DateTime.UtcNow.AddMonths(-3), CompanyId = 1 },
            new VehicleCost { Id = 3, VehicleId = 1, Type = "fuel", Amount = 50m, Date = DateTime.UtcNow.AddDays(-1), CompanyId = 1 } // Not a document
        );
        await context.SaveChangesAsync();

        var handler = new GetRenewalHistoryQueryHandler(context);
        var query = new GetRenewalHistoryQuery(VehicleId: 1);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2); // Only document types
        result.Should().Contain(r => r.DocumentType == "insurance");
        result.Should().Contain(r => r.DocumentType == "technical_inspection");
    }

    [Fact]
    public async Task GetExpiryAlerts_ReturnsExpiringDocuments()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "V1", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(-5) }, // Expired
            new Vehicle { Id = 2, Name = "V2", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(20) }, // Expiring in 20 days
            new Vehicle { Id = 3, Name = "V3", CompanyId = 1, InsuranceExpiry = DateTime.UtcNow.AddDays(60) }  // OK, not in alert range
        );
        await context.SaveChangesAsync();

        var handler = new GetExpiryAlertsQueryHandler(context);
        var query = new GetExpiryAlertsQuery(DaysThreshold: 30);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2); // V1 (expired) and V2 (expiring soon)
    }
}


