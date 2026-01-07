using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;

namespace GisAPI.Tests.Application.Vehicles;

public class UpdateVehicleCommandTests
{
    [Fact]
    public async Task Handle_ValidCommand_ShouldUpdateVehicle()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        var vehicle = TestDataBuilder.CreateVehicle(companyId: 1);

        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new UpdateVehicleCommandHandler(context, tenantService.Object);
        var command = new UpdateVehicleCommand(
            Id: vehicle.Id,
            Name: "Updated Vehicle Name",
            Type: "voiture",
            Brand: "BMW",
            Model: "X5",
            Plate: "99999-Z-9",
            Year: 2024,
            Color: "Black",
            Status: "in_use",
            Mileage: 75000,
            AssignedDriverId: null,
            AssignedSupervisorId: null
        );

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var updatedVehicle = await context.Vehicles.FindAsync(vehicle.Id);
        updatedVehicle.Should().NotBeNull();
        updatedVehicle!.Name.Should().Be("Updated Vehicle Name");
        updatedVehicle.Type.Should().Be("voiture");
        updatedVehicle.Brand.Should().Be("BMW");
        updatedVehicle.Status.Should().Be("in_use");
        updatedVehicle.Mileage.Should().Be(75000);
    }

    [Fact]
    public async Task Handle_VehicleNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var handler = new UpdateVehicleCommandHandler(context, tenantService.Object);
        var command = new UpdateVehicleCommand(
            Id: 999,
            Name: "Non-existent",
            Type: "camion",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Status: "available",
            Mileage: 0,
            AssignedDriverId: null,
            AssignedSupervisorId: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => 
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_VehicleFromDifferentCompany_ShouldThrowNotFoundException()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var subscription = TestDataBuilder.CreateSubscription();
        var company1 = TestDataBuilder.CreateCompany(id: 1, subscriptionId: subscription.Id);
        var company2 = TestDataBuilder.CreateCompany(id: 2, subscriptionId: subscription.Id);
        var vehicle = TestDataBuilder.CreateVehicle(id: 1, companyId: 2); // Different company

        context.Subscriptions.Add(subscription);
        context.Companies.AddRange(company1, company2);
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new UpdateVehicleCommandHandler(context, tenantService.Object);
        var command = new UpdateVehicleCommand(
            Id: vehicle.Id,
            Name: "Unauthorized Update",
            Type: "camion",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Status: "available",
            Mileage: 0,
            AssignedDriverId: null,
            AssignedSupervisorId: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => 
            handler.Handle(command, CancellationToken.None));
    }
}
