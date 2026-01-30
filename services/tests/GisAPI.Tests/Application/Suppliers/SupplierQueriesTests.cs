using FluentAssertions;
using GisAPI.Application.Features.Suppliers.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Suppliers;

public class SupplierQueriesTests
{
    [Fact]
    public async Task GetSuppliers_WithNoData_ReturnsEmptyList()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new GetSuppliersQueryHandler(context);
        var query = new GetSuppliersQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetSuppliers_WithData_ReturnsSuppliers()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 1, Name = "Garage A", Type = "garage", CompanyId = 1, IsActive = true },
            new Supplier { Id = 2, Name = "Supplier B", Type = "supplier", CompanyId = 1, IsActive = true },
            new Supplier { Id = 3, Name = "Garage C", Type = "garage", CompanyId = 1, IsActive = false }
        );
        await context.SaveChangesAsync();

        var handler = new GetSuppliersQueryHandler(context);
        var query = new GetSuppliersQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetSuppliers_WithTypeFilter_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 1, Name = "Garage A", Type = "garage", CompanyId = 1 },
            new Supplier { Id = 2, Name = "Supplier B", Type = "supplier", CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var handler = new GetSuppliersQueryHandler(context);
        var query = new GetSuppliersQuery(Type: "garage");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.First().Type.Should().Be("garage");
    }

    [Fact]
    public async Task GetSuppliers_WithSearchTerm_SearchesByName()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 1, Name = "Auto Service Plus", Type = "garage", CompanyId = 1 },
            new Supplier { Id = 2, Name = "Parts Warehouse", Type = "supplier", CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var handler = new GetSuppliersQueryHandler(context);
        var query = new GetSuppliersQuery(SearchTerm: "Auto");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.First().Name.Should().Contain("Auto");
    }

    [Fact]
    public async Task GetSupplierStats_ReturnsCorrectStats()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 1, Name = "Garage A", Type = "garage", CompanyId = 1, IsActive = true },
            new Supplier { Id = 2, Name = "Garage B", Type = "garage", CompanyId = 1, IsActive = true },
            new Supplier { Id = 3, Name = "Supplier C", Type = "supplier", CompanyId = 1, IsActive = false }
        );
        await context.SaveChangesAsync();

        var handler = new GetSupplierStatsQueryHandler(context);
        var query = new GetSupplierStatsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Total.Should().Be(3);
        result.Active.Should().Be(2);
        result.ByType["garage"].Should().Be(2);
        result.ByType["supplier"].Should().Be(1);
    }

    [Fact]
    public async Task GetGarages_ReturnsOnlyGarages()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Suppliers.AddRange(
            new Supplier { Id = 1, Name = "Garage A", Type = "garage", CompanyId = 1 },
            new Supplier { Id = 2, Name = "Supplier B", Type = "supplier", CompanyId = 1 },
            new Supplier { Id = 3, Name = "Garage C", Type = "garage", CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var handler = new GetGaragesQueryHandler(context);
        var query = new GetGaragesQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().AllSatisfy(s => s.Type.Should().Be("garage"));
    }
}


