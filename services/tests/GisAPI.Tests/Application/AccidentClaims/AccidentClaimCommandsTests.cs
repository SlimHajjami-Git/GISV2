using FluentAssertions;
using GisAPI.Application.Features.AccidentClaims.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.AccidentClaims;

public class AccidentClaimCommandsTests
{
    [Fact]
    public async Task CreateAccidentClaim_WithValidData_ReturnsId()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1 });
        await context.SaveChangesAsync();

        var handler = new CreateAccidentClaimCommandHandler(context, tenantService.Object);
        var command = new CreateAccidentClaimCommand(
            VehicleId: 1,
            DriverId: null,
            AccidentDate: DateTime.UtcNow.Date,
            AccidentTime: "14:30",
            Location: "Avenue Habib Bourguiba, Tunis",
            Latitude: 36.8,
            Longitude: 10.18,
            Description: "Minor collision at intersection",
            Severity: "minor",
            EstimatedDamage: 1500m,
            DamagedZones: new[] { "Avant", "Pare-chocs" },
            ThirdPartyInvolved: false,
            ThirdPartyName: null,
            ThirdPartyPhone: null,
            ThirdPartyVehiclePlate: null,
            ThirdPartyVehicleModel: null,
            ThirdPartyInsurance: null,
            ThirdPartyInsuranceNumber: null,
            PoliceReportNumber: null,
            MileageAtAccident: 45000,
            Witnesses: null,
            AdditionalNotes: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeGreaterThan(0);
        var claim = await context.AccidentClaims.FindAsync(result);
        claim.Should().NotBeNull();
        claim!.ClaimNumber.Should().StartWith("SIN-");
        claim.Status.Should().Be("draft");
        claim.Severity.Should().Be("minor");
    }

    [Fact]
    public async Task CreateAccidentClaim_WithThirdParty_CreatesThirdPartyRecord()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var tenantService = TestDbContextFactory.CreateMockTenantService(companyId: 1);
        
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Test Vehicle", CompanyId = 1 });
        await context.SaveChangesAsync();

        var handler = new CreateAccidentClaimCommandHandler(context, tenantService.Object);
        var command = new CreateAccidentClaimCommand(
            VehicleId: 1,
            DriverId: null,
            AccidentDate: DateTime.UtcNow.Date,
            AccidentTime: "10:00",
            Location: "Route de Sousse",
            Latitude: null,
            Longitude: null,
            Description: "Rear-end collision",
            Severity: "moderate",
            EstimatedDamage: 5000m,
            DamagedZones: null,
            ThirdPartyInvolved: true,
            ThirdPartyName: "Ahmed Ben Ali",
            ThirdPartyPhone: "+216 98 765 432",
            ThirdPartyVehiclePlate: "123 TUN 456",
            ThirdPartyVehicleModel: "Peugeot 208",
            ThirdPartyInsurance: "STAR Assurances",
            ThirdPartyInsuranceNumber: "POL-12345",
            PoliceReportNumber: "PV-2026-001",
            MileageAtAccident: null,
            Witnesses: null,
            AdditionalNotes: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        var thirdParties = context.AccidentClaimThirdParties.Where(tp => tp.ClaimId == result).ToList();
        thirdParties.Should().HaveCount(1);
        thirdParties.First().Name.Should().Be("Ahmed Ben Ali");
        thirdParties.First().InsuranceCompany.Should().Be("STAR Assurances");
    }

    [Fact]
    public async Task SubmitAccidentClaim_FromDraft_ChangesStatusToSubmitted()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var claim = new AccidentClaim
        {
            Id = 1,
            ClaimNumber = "SIN-2026-0001",
            VehicleId = 1,
            CompanyId = 1,
            AccidentDate = DateTime.UtcNow.Date,
            Location = "Test",
            Description = "Test",
            Severity = "minor",
            Status = "draft",
            EstimatedDamage = 1000m
        };
        context.AccidentClaims.Add(claim);
        await context.SaveChangesAsync();

        var handler = new SubmitAccidentClaimCommandHandler(context);
        var command = new SubmitAccidentClaimCommand(Id: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var updated = await context.AccidentClaims.FindAsync(1);
        updated!.Status.Should().Be("submitted");
    }

    [Fact]
    public async Task ApproveAccidentClaim_FromSubmitted_SetsApprovedAmount()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var claim = new AccidentClaim
        {
            Id = 1,
            ClaimNumber = "SIN-2026-0001",
            VehicleId = 1,
            CompanyId = 1,
            AccidentDate = DateTime.UtcNow.Date,
            Location = "Test",
            Description = "Test",
            Severity = "moderate",
            Status = "submitted",
            EstimatedDamage = 5000m
        };
        context.AccidentClaims.Add(claim);
        await context.SaveChangesAsync();

        var handler = new ApproveAccidentClaimCommandHandler(context);
        var command = new ApproveAccidentClaimCommand(Id: 1, ApprovedAmount: 4500m);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var updated = await context.AccidentClaims.FindAsync(1);
        updated!.Status.Should().Be("approved");
        updated.ApprovedAmount.Should().Be(4500m);
    }

    [Fact]
    public async Task DeleteAccidentClaim_OnlyAllowedForDrafts()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var claim = new AccidentClaim
        {
            Id = 1,
            ClaimNumber = "SIN-2026-0001",
            VehicleId = 1,
            CompanyId = 1,
            AccidentDate = DateTime.UtcNow.Date,
            Location = "Test",
            Description = "Test",
            Severity = "minor",
            Status = "submitted", // Not draft
            EstimatedDamage = 1000m
        };
        context.AccidentClaims.Add(claim);
        await context.SaveChangesAsync();

        var handler = new DeleteAccidentClaimCommandHandler(context);
        var command = new DeleteAccidentClaimCommand(Id: 1);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task CloseAccidentClaim_OnlyAllowedForApproved()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var claim = new AccidentClaim
        {
            Id = 1,
            ClaimNumber = "SIN-2026-0001",
            VehicleId = 1,
            CompanyId = 1,
            AccidentDate = DateTime.UtcNow.Date,
            Location = "Test",
            Description = "Test",
            Severity = "minor",
            Status = "approved",
            EstimatedDamage = 1000m,
            ApprovedAmount = 900m
        };
        context.AccidentClaims.Add(claim);
        await context.SaveChangesAsync();

        var handler = new CloseAccidentClaimCommandHandler(context);
        var command = new CloseAccidentClaimCommand(Id: 1);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        var updated = await context.AccidentClaims.FindAsync(1);
        updated!.Status.Should().Be("closed");
    }
}


