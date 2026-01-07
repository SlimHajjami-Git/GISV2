using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicles;
using GisAPI.Tests.Common;

namespace GisAPI.Tests.Application.Vehicles;

public class GetVehiclesQueryTests
{
    [Fact]
    public async Task Handle_ShouldReturnVehiclesForTenant()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        // Setup data
        var subscription = TestDataBuilder.CreateSubscription();
        var company1 = TestDataBuilder.CreateCompany(id: 1, subscriptionId: subscription.Id);
        var company2 = TestDataBuilder.CreateCompany(id: 2, subscriptionId: subscription.Id);
        company2.Name = "Other Company";

        context.Subscriptions.Add(subscription);
        context.Companies.AddRange(company1, company2);

        // Add vehicles to different companies
        var vehicle1 = TestDataBuilder.CreateVehicle(id: 1, companyId: 1, name: "Vehicle 1");
        var vehicle2 = TestDataBuilder.CreateVehicle(id: 2, companyId: 1, name: "Vehicle 2");
        var vehicle3 = TestDataBuilder.CreateVehicle(id: 3, companyId: 2, name: "Other Company Vehicle");

        context.Vehicles.AddRange(vehicle1, vehicle2, vehicle3);
        await context.SaveChangesAsync();

        var handler = new GetVehiclesQueryHandler(context, tenantService.Object);
        var query = new GetVehiclesQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(v => v.Name.Should().NotBe("Other Company Vehicle"));
    }

    [Fact]
    public async Task Handle_WithSearchTerm_ShouldFilterByName()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);

        var vehicle1 = TestDataBuilder.CreateVehicle(id: 1, companyId: 1, name: "Mercedes Actros");
        var vehicle2 = TestDataBuilder.CreateVehicle(id: 2, companyId: 1, name: "Volvo FH16");
        var vehicle3 = TestDataBuilder.CreateVehicle(id: 3, companyId: 1, name: "Mercedes Sprinter");

        context.Vehicles.AddRange(vehicle1, vehicle2, vehicle3);
        await context.SaveChangesAsync();

        var handler = new GetVehiclesQueryHandler(context, tenantService.Object);
        var query = new GetVehiclesQuery(SearchTerm: "Mercedes");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(v => v.Name.Should().Contain("Mercedes"));
    }

    [Fact]
    public async Task Handle_WithStatusFilter_ShouldFilterByStatus()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);

        var vehicle1 = TestDataBuilder.CreateVehicle(id: 1, companyId: 1, name: "Available Vehicle");
        vehicle1.Status = "available";
        
        var vehicle2 = TestDataBuilder.CreateVehicle(id: 2, companyId: 1, name: "In Use Vehicle");
        vehicle2.Status = "in_use";
        
        var vehicle3 = TestDataBuilder.CreateVehicle(id: 3, companyId: 1, name: "Maintenance Vehicle");
        vehicle3.Status = "maintenance";

        context.Vehicles.AddRange(vehicle1, vehicle2, vehicle3);
        await context.SaveChangesAsync();

        var handler = new GetVehiclesQueryHandler(context, tenantService.Object);
        var query = new GetVehiclesQuery(Status: "available");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items.First().Status.Should().Be("available");
    }

    [Fact]
    public async Task Handle_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var context = TestDbContextFactory.CreateWithTenant(companyId: 1);

        var subscription = TestDataBuilder.CreateSubscription();
        var company = TestDataBuilder.CreateCompany(subscriptionId: subscription.Id);
        context.Subscriptions.Add(subscription);
        context.Companies.Add(company);

        // Add 10 vehicles
        for (int i = 1; i <= 10; i++)
        {
            var vehicle = TestDataBuilder.CreateVehicle(id: i, companyId: 1, name: $"Vehicle {i:D2}");
            context.Vehicles.Add(vehicle);
        }
        await context.SaveChangesAsync();

        var handler = new GetVehiclesQueryHandler(context, tenantService.Object);
        var query = new GetVehiclesQuery(Page: 2, PageSize: 3);

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(3);
        result.PageNumber.Should().Be(2);
        result.TotalCount.Should().Be(10);
        result.TotalPages.Should().Be(4); // 10 / 3 = 4 pages
    }
}
