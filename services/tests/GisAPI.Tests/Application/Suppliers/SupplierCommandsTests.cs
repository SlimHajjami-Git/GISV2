using FluentAssertions;
using GisAPI.Application.Features.Suppliers.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Suppliers;

public class SupplierCommandsTests
{
    [Fact]
    public async Task CreateSupplier_WithValidData_ReturnsId()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var handler = new CreateSupplierCommandHandler(context, tenantService.Object);

        var command = new CreateSupplierCommand(
            "Test Garage",
            "garage",
            "123 Test Street",
            "Test City",
            null,
            "John Doe",
            "+216 12 345 678",
            "test@garage.com",
            null, null, null, null, null, null, null,
            true,
            new List<string> { "mecanique", "carrosserie" }
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        var supplier = await context.Suppliers.FindAsync(result);
        supplier.Should().NotBeNull();
        supplier!.Name.Should().Be("Test Garage");
        supplier.Type.Should().Be("garage");
        supplier.CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task CreateSupplier_WithServices_CreatesSupplierServices()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var handler = new CreateSupplierCommandHandler(context, tenantService.Object);

        var command = new CreateSupplierCommand(
            "Multi-Service Garage",
            "garage",
            null, null, null, null, null, null, null, null, null, null, null, null, null,
            true,
            new List<string> { "mecanique", "electricite", "pneumatique" }
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        var services = context.SupplierServices.Where(s => s.SupplierId == result).ToList();
        services.Should().HaveCount(3);
        services.Select(s => s.ServiceCode).Should().Contain("mecanique");
        services.Select(s => s.ServiceCode).Should().Contain("electricite");
    }

    [Fact]
    public async Task UpdateSupplier_WithValidData_UpdatesSupplier()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var supplier = new Supplier { Id = 1, Name = "Old Name", Type = "garage", CompanyId = 1 };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var handler = new UpdateSupplierCommandHandler(context);
        var command = new UpdateSupplierCommand(1, "New Name", null, null, "New City", null, null, null, null, null, null, null, null, null, null, null, null, null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var updated = await context.Suppliers.FindAsync(1);
        updated!.Name.Should().Be("New Name");
        updated.City.Should().Be("New City");
    }

    [Fact]
    public async Task UpdateSupplier_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var handler = new UpdateSupplierCommandHandler(context);
        var command = new UpdateSupplierCommand(999, "New Name", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSupplier_WithValidId_DeletesSupplier()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var supplier = new Supplier { Id = 1, Name = "To Delete", Type = "supplier", CompanyId = 1 };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var handler = new DeleteSupplierCommandHandler(context);
        var command = new DeleteSupplierCommand(Id: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var deleted = await context.Suppliers.FindAsync(1);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSupplierServices_ReplacesExistingServices()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var supplier = new Supplier { Id = 1, Name = "Garage", Type = "garage", CompanyId = 1 };
        context.Suppliers.Add(supplier);
        context.SupplierServices.Add(new SupplierService { SupplierId = 1, ServiceCode = "mecanique" });
        await context.SaveChangesAsync();

        var handler = new UpdateSupplierServicesCommandHandler(context);
        var command = new UpdateSupplierServicesCommand(1, new List<string> { "electricite", "pneumatique" });

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var services = context.SupplierServices.Where(s => s.SupplierId == 1).ToList();
        services.Should().HaveCount(2);
        services.Select(s => s.ServiceCode).Should().NotContain("mecanique");
        services.Select(s => s.ServiceCode).Should().Contain("electricite");
    }
}
