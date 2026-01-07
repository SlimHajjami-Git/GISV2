using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.DeleteVehicle;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Tests.Application.Vehicles;

public class DeleteVehicleCommandTests
{
    [Fact]
    public async Task Handle_ValidCommand_ShouldDeleteVehicle()
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

        var vehicleId = vehicle.Id;
        var handler = new DeleteVehicleCommandHandler(context, tenantService.Object);
        var command = new DeleteVehicleCommand(vehicleId);

        // Act
        await handler.Handle(command, CancellationToken.None);

        // Assert
        var deletedVehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == vehicleId);
        deletedVehicle.Should().BeNull();
    }

    [Fact]
    public async Task Handle_VehicleNotFound_ShouldThrowNotFoundException()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var handler = new DeleteVehicleCommandHandler(context, tenantService.Object);
        var command = new DeleteVehicleCommand(999);

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

        var handler = new DeleteVehicleCommandHandler(context, tenantService.Object);
        var command = new DeleteVehicleCommand(vehicle.Id);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() => 
            handler.Handle(command, CancellationToken.None));
    }
}
