using FluentAssertions;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Features.VehicleMaintenance.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

public class VehicleMaintenanceTests
{
    [Fact]
    public async Task AssignMaintenanceTemplate_CreatesSchedule()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1, Mileage = 50000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate 
        { 
            Id = 1, 
            Name = "Vidange", 
            Category = "Moteur",
            Priority = "medium",
            IntervalKm = 10000,
            IntervalMonths = 6,
            CompanyId = 1 
        });
        await context.SaveChangesAsync();

        var handler = new AssignMaintenanceTemplateCommandHandler(context);
        var command = new AssignMaintenanceTemplateCommand(VehicleId: 1, TemplateId: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        var schedule = await context.VehicleMaintenanceSchedules.FindAsync(result);
        schedule.Should().NotBeNull();
        schedule!.NextDueKm.Should().Be(60000); // 50000 + 10000
        schedule.Status.Should().Be("upcoming");
    }

    [Fact]
    public async Task AssignMaintenanceTemplate_AlreadyAssigned_ReturnsSameId()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1, Mileage = 50000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate 
        { 
            Id = 1, 
            Name = "Vidange", 
            Category = "Moteur",
            Priority = "medium",
            IntervalKm = 10000,
            CompanyId = 1 
        });
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule
        {
            Id = 99,
            VehicleId = 1,
            TemplateId = 1,
            NextDueKm = 60000,
            Status = "upcoming"
        });
        await context.SaveChangesAsync();

        var handler = new AssignMaintenanceTemplateCommandHandler(context);
        var command = new AssignMaintenanceTemplateCommand(VehicleId: 1, TemplateId: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().Be(99); // Same ID returned
    }

    [Fact]
    public async Task MarkMaintenanceDone_CreatesLogAndCost()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1, Mileage = 50000 });
        context.MaintenanceTemplates.Add(new MaintenanceTemplate 
        { 
            Id = 1, 
            Name = "Vidange", 
            Category = "Moteur",
            Priority = "medium",
            IntervalKm = 10000,
            IntervalMonths = 6,
            CompanyId = 1 
        });
        await context.SaveChangesAsync();

        var handler = new MarkMaintenanceDoneCommandHandler(context, tenantService.Object);
        var command = new MarkMaintenanceDoneCommand(
            VehicleId: 1,
            TemplateId: 1,
            Date: DateTime.UtcNow.Date,
            Mileage: 55000,
            Cost: 180m,
            SupplierId: null,
            Notes: "Huile 5W30"
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        
        // Check MaintenanceLog created
        var log = await context.MaintenanceLogs.FindAsync(result);
        log.Should().NotBeNull();
        log!.DoneKm.Should().Be(55000);
        log.ActualCost.Should().Be(180m);
        
        // Check VehicleCost created
        var costs = context.VehicleCosts.Where(c => c.VehicleId == 1).ToList();
        costs.Should().HaveCount(1);
        costs.First().Type.Should().Be("maintenance");
        costs.First().Amount.Should().Be(180m);
        
        // Check schedule updated
        var schedule = context.VehicleMaintenanceSchedules.FirstOrDefault(s => s.VehicleId == 1 && s.TemplateId == 1);
        schedule.Should().NotBeNull();
        schedule!.LastDoneKm.Should().Be(55000);
        schedule.NextDueKm.Should().Be(65000); // 55000 + 10000
    }

    [Fact]
    public async Task GetMaintenanceAlerts_ReturnsOverdueAndDue()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1, Mileage = 60000 });
        context.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 1, Name = "Vidange", Category = "Moteur", Priority = "medium", IntervalKm = 10000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 2, Name = "Freins", Category = "Freinage", Priority = "high", IntervalKm = 30000, CompanyId = 1 }
        );
        context.VehicleMaintenanceSchedules.AddRange(
            new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, NextDueKm = 55000, Status = "overdue" },
            new VehicleMaintenanceSchedule { Id = 2, VehicleId = 1, TemplateId = 2, NextDueKm = 60500, Status = "due" }
        );
        await context.SaveChangesAsync();

        var handler = new GetMaintenanceAlertsQueryHandler(context);
        var query = new GetMaintenanceAlertsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(a => a.Status == "overdue");
        result.Should().Contain(a => a.Status == "due");
    }

    [Fact]
    public async Task GetMaintenanceStats_ReturnsCorrectCounts()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.VehicleMaintenanceSchedules.AddRange(
            new VehicleMaintenanceSchedule { Id = 1, VehicleId = 1, TemplateId = 1, Status = "overdue" },
            new VehicleMaintenanceSchedule { Id = 2, VehicleId = 1, TemplateId = 2, Status = "due" },
            new VehicleMaintenanceSchedule { Id = 3, VehicleId = 2, TemplateId = 1, Status = "upcoming" },
            new VehicleMaintenanceSchedule { Id = 4, VehicleId = 2, TemplateId = 2, Status = "ok" }
        );
        await context.SaveChangesAsync();

        var handler = new GetMaintenanceStatsQueryHandler(context);
        var query = new GetMaintenanceStatsQuery();

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.TotalSchedules.Should().Be(4);
        result.OverdueCount.Should().Be(1);
        result.DueCount.Should().Be(1);
        result.UpcomingCount.Should().Be(1);
        result.OkCount.Should().Be(1);
    }

    [Fact]
    public async Task RemoveMaintenanceSchedule_DeletesSchedule()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        context.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule 
        { 
            Id = 1, 
            VehicleId = 1, 
            TemplateId = 1, 
            Status = "upcoming" 
        });
        await context.SaveChangesAsync();

        var handler = new RemoveMaintenanceScheduleCommandHandler(context);
        var command = new RemoveMaintenanceScheduleCommand(ScheduleId: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var deleted = await context.VehicleMaintenanceSchedules.FindAsync(1);
        deleted.Should().BeNull();
    }
}


