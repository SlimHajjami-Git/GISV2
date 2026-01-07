using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Tests.Common;

namespace GisAPI.Tests.Application.Vehicles;

public class CreateVehicleCommandTests
{
    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateVehicle()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);
        
        // Add required company
        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new CreateVehicleCommandHandler(context, tenantService.Object);
        var command = new CreateVehicleCommand(
            Name: "New Vehicle",
            Type: "camion",
            Brand: "Volvo",
            Model: "FH16",
            Plate: "54321-B-2",
            Year: 2024,
            Color: "Blue",
            Mileage: 0
        );

        // Act
        var vehicleId = await handler.Handle(command, CancellationToken.None);

        // Assert
        vehicleId.Should().BeGreaterThan(0);

        var vehicle = await context.Vehicles.FindAsync(vehicleId);
        vehicle.Should().NotBeNull();
        vehicle!.Name.Should().Be("New Vehicle");
        vehicle.Type.Should().Be("camion");
        vehicle.Brand.Should().Be("Volvo");
        vehicle.Plate.Should().Be("54321-B-2");
        vehicle.CompanyId.Should().Be(1);
        vehicle.Status.Should().Be("available");
    }

    [Fact]
    public async Task Handle_ShouldSetCompanyIdFromTenantService()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 5);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 5);
        
        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(id: 5, subscriptionId: subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);
        await context.SaveChangesAsync();

        var handler = new CreateVehicleCommandHandler(context, tenantService.Object);
        var command = new CreateVehicleCommand(
            Name: "Company 5 Vehicle",
            Type: "voiture",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Mileage: 1000
        );

        // Act
        var vehicleId = await handler.Handle(command, CancellationToken.None);

        // Assert
        var vehicle = await context.Vehicles.FindAsync(vehicleId);
        vehicle!.CompanyId.Should().Be(5);
    }
}

public class CreateVehicleCommandValidatorTests
{
    private readonly CreateVehicleCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        // Arrange
        var command = new CreateVehicleCommand(
            Name: "Valid Vehicle",
            Type: "camion",
            Brand: "Mercedes",
            Model: "Actros",
            Plate: "12345-A-1",
            Year: 2023,
            Color: "White",
            Mileage: 50000
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyName_ShouldFail()
    {
        // Arrange
        var command = new CreateVehicleCommand(
            Name: "",
            Type: "camion",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Mileage: 0
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public void Validate_EmptyType_ShouldFail()
    {
        // Arrange
        var command = new CreateVehicleCommand(
            Name: "Test Vehicle",
            Type: "",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Mileage: 0
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Type");
    }

    [Fact]
    public void Validate_NegativeMileage_ShouldFail()
    {
        // Arrange
        var command = new CreateVehicleCommand(
            Name: "Test Vehicle",
            Type: "camion",
            Brand: null,
            Model: null,
            Plate: null,
            Year: null,
            Color: null,
            Mileage: -100
        );

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Mileage");
    }
}
