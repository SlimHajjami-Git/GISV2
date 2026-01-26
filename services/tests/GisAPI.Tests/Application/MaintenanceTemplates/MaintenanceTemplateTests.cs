using FluentAssertions;
using GisAPI.Application.Features.MaintenanceTemplates.Commands;
using GisAPI.Application.Features.MaintenanceTemplates.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

public class MaintenanceTemplateTests
{
    [Fact]
    public async Task CreateMaintenanceTemplate_WithValidData_ReturnsId()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var handler = new CreateMaintenanceTemplateCommandHandler(context, tenantService.Object);

        var command = new CreateMaintenanceTemplateCommand(
            Name: "Vidange huile moteur",
            Description: "Changement d'huile et filtre",
            Category: "Moteur",
            Priority: "medium",
            IntervalKm: 10000,
            IntervalMonths: 6,
            EstimatedCost: 150m,
            IsActive: true
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        var template = await context.MaintenanceTemplates.FindAsync(result);
        template.Should().NotBeNull();
        template!.Name.Should().Be("Vidange huile moteur");
        template.IntervalKm.Should().Be(10000);
    }

    [Fact]
    public async Task CreateMaintenanceTemplate_WithNoInterval_ThrowsException()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        var handler = new CreateMaintenanceTemplateCommandHandler(context, tenantService.Object);

        var command = new CreateMaintenanceTemplateCommand(
            Name: "Invalid Template",
            Description: null,
            Category: "Moteur",
            Priority: "medium",
            IntervalKm: null,
            IntervalMonths: null, // No interval specified
            EstimatedCost: null,
            IsActive: true
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => 
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task GetMaintenanceTemplates_WithData_ReturnsTemplates()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 10000, CompanyId = 1, IsActive = true },
            new MaintenanceTemplate { Id = 2, Name = "Freins", Category = "Freinage", Priority = "high", IntervalKm = 30000, CompanyId = 1, IsActive = true },
            new MaintenanceTemplate { Id = 3, Name = "Pneus", Category = "Pneumatique", Priority = "medium", IntervalMonths = 24, CompanyId = 1, IsActive = false }
        );
        await context.SaveChangesAsync();

        var handler = new GetMaintenanceTemplatesQueryHandler(context);
        var query = new GetMaintenanceTemplatesQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetMaintenanceTemplates_WithCategoryFilter_FiltersResults()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 10000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 2, Name = "Freins", Category = "Freinage", Priority = "high", IntervalKm = 30000, CompanyId = 1 }
        );
        await context.SaveChangesAsync();

        var handler = new GetMaintenanceTemplatesQueryHandler(context);
        var query = new GetMaintenanceTemplatesQuery(Category: "Moteur");

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.First().Category.Should().Be("Moteur");
    }

    [Fact]
    public async Task UpdateMaintenanceTemplate_WithValidData_UpdatesTemplate()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.Add(new MaintenanceTemplate 
        { 
            Id = 1, 
            Name = "Old Name", 
            Category = "Moteur", 
            Priority = "low",
            IntervalKm = 5000,
            CompanyId = 1 
        });
        await context.SaveChangesAsync();

        var handler = new UpdateMaintenanceTemplateCommandHandler(context);
        var command = new UpdateMaintenanceTemplateCommand(
            Id: 1, 
            Name: "New Name", 
            Description: "Updated description",
            Category: null,
            Priority: "high",
            IntervalKm: 10000,
            IntervalMonths: null,
            EstimatedCost: 200m,
            IsActive: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var updated = await context.MaintenanceTemplates.FindAsync(1);
        updated!.Name.Should().Be("New Name");
        updated.Priority.Should().Be("high");
        updated.IntervalKm.Should().Be(10000);
    }

    [Fact]
    public async Task DeleteMaintenanceTemplate_WithValidId_DeletesTemplate()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.MaintenanceTemplates.Add(new MaintenanceTemplate 
        { 
            Id = 1, 
            Name = "To Delete", 
            Category = "Moteur",
            Priority = "low",
            IntervalKm = 5000,
            CompanyId = 1 
        });
        await context.SaveChangesAsync();

        var handler = new DeleteMaintenanceTemplateCommandHandler(context);
        var command = new DeleteMaintenanceTemplateCommand(Id: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var deleted = await context.MaintenanceTemplates.FindAsync(1);
        deleted.Should().BeNull();
    }
}
