using FluentAssertions;
using GisAPI.Application.Features.Reservations;
using GisAPI.Application.Features.Reservations.Commands.CreateReservation;
using GisAPI.Application.Features.Reservations.Commands.CompleteReservation;
using GisAPI.Application.Features.Reservations.Commands.CancelReservation;
using GisAPI.Application.Features.Reservations.Commands.SetVehicleRentalStatus;
using GisAPI.Application.Features.Reservations.Queries.GetReservations;
using GisAPI.Application.Features.Reservations.Queries.GetAvailableVehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Reservations;

public class ReservationCommandsTests
{
    private const int CompanyId = 1;
    private const int UserId = 10;

    private (TestGisDbContext context, Moq.Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> tenant) SetupContext()
    {
        var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: UserId);

        // Add a location-type company
        context.Societes.Add(new Societe { Id = CompanyId, Name = "Test Location Co", Type = "location", IsActive = true });

        // Add users
        context.Users.Add(new User { Id = UserId, FirstName = "Jean", LastName = "Dupont", Email = "jean@test.com", CompanyId = CompanyId, PasswordHash = "hash" });
        context.Users.Add(new User { Id = 20, FirstName = "Marie", LastName = "Martin", Email = "marie@test.com", CompanyId = CompanyId, PasswordHash = "hash" });

        // Add vehicles
        context.Vehicles.Add(new Vehicle { Id = 1, Name = "Peugeot 208", Plate = "TU-1234", Mileage = 50000, HasGps = true, CompanyId = CompanyId });
        context.Vehicles.Add(new Vehicle { Id = 2, Name = "Renault Clio", Plate = "TU-5678", Mileage = 30000, HasGps = false, CompanyId = CompanyId });
        context.Vehicles.Add(new Vehicle { Id = 3, Name = "Dacia Duster", Plate = "TU-9012", Mileage = 75000, HasGps = true, IsRented = true, CompanyId = CompanyId });

        context.SaveChanges();
        return (context, tenant);
    }

    // ── CreateReservation ──

    [Fact]
    public async Task CreateReservation_WithAvailableVehicle_Succeeds()
    {
        var (context, tenant) = SetupContext();
        var handler = new CreateReservationCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new CreateReservationCommand(
            VehicleId: 1,
            AssignedDriverId: 20,
            Purpose: "Livraison client",
            Destination: "Tunis",
            EstimatedKm: 100,
            Notes: null
        ), CancellationToken.None);

        result.Should().NotBeNull();
        result.VehicleId.Should().Be(1);
        result.VehicleName.Should().Be("Peugeot 208");
        result.Status.Should().Be("in_progress");
        result.StartMileage.Should().Be(50000); // fallback to vehicle.Mileage when no GPS positions
        result.Purpose.Should().Be("Livraison client");

        var reservation = await context.Reservations.FindAsync(result.Id);
        reservation.Should().NotBeNull();
        reservation!.CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task CreateReservation_WithRentedVehicle_ThrowsConflict()
    {
        var (context, tenant) = SetupContext();
        var handler = new CreateReservationCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new CreateReservationCommand(
            VehicleId: 3, // IsRented = true
            AssignedDriverId: null,
            Purpose: null,
            Destination: null,
            EstimatedKm: null,
            Notes: null
        ), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*loué*");
    }

    [Fact]
    public async Task CreateReservation_WithActiveReservationOnVehicle_ThrowsConflict()
    {
        var (context, tenant) = SetupContext();

        // Add an active reservation on vehicle 2
        context.Reservations.Add(new Reservation
        {
            VehicleId = 2,
            RequestedByUserId = UserId,
            Status = "in_progress",
            StartDateTime = DateTime.UtcNow.AddHours(-2),
            EndDateTime = DateTime.UtcNow.AddDays(1),
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new CreateReservationCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new CreateReservationCommand(
            VehicleId: 2,
            AssignedDriverId: null,
            Purpose: null,
            Destination: null,
            EstimatedKm: null,
            Notes: null
        ), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*emprunt en cours*");
    }

    [Fact]
    public async Task CreateReservation_WithNonLocationCompany_ThrowsDomainException()
    {
        var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 99, userId: UserId);

        context.Societes.Add(new Societe { Id = 99, Name = "Transport Co", Type = "transport", IsActive = true });
        context.Vehicles.Add(new Vehicle { Id = 10, Name = "Truck", CompanyId = 99 });
        context.SaveChanges();

        var handler = new CreateReservationCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new CreateReservationCommand(
            VehicleId: 10, AssignedDriverId: null, Purpose: null, Destination: null, EstimatedKm: null, Notes: null
        ), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*location*");
    }

    [Fact]
    public async Task CreateReservation_WithGpsOdometer_CapturesMileageFromGps()
    {
        var (context, tenant) = SetupContext();

        // Add a GPS device and positions for vehicle 1
        context.GpsDevices.Add(new GpsDevice { Id = 100, DeviceUid = "123456789", CompanyId = CompanyId });

        var vehicle = await context.Vehicles.FindAsync(1);
        vehicle!.GpsDeviceId = 100;
        await context.SaveChangesAsync();

        // Add GPS positions with odometer
        context.GpsPositions.Add(new GpsPosition { DeviceId = 100, OdometerKm = 52340, RecordedAt = DateTime.UtcNow.AddMinutes(-5), Latitude = 36.8, Longitude = 10.1 });
        context.GpsPositions.Add(new GpsPosition { DeviceId = 100, OdometerKm = 52345, RecordedAt = DateTime.UtcNow.AddMinutes(-1), Latitude = 36.8, Longitude = 10.1 });
        await context.SaveChangesAsync();

        var handler = new CreateReservationCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new CreateReservationCommand(
            VehicleId: 1, AssignedDriverId: null, Purpose: "Test GPS", Destination: null, EstimatedKm: null, Notes: null
        ), CancellationToken.None);

        result.StartMileage.Should().Be(52345); // latest GPS odometer
    }

    // ── CompleteReservation ──

    [Fact]
    public async Task CompleteReservation_WithActiveReservation_Completes()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation
        {
            Id = 1,
            VehicleId = 2,
            RequestedByUserId = UserId,
            Status = "in_progress",
            StartDateTime = DateTime.UtcNow.AddDays(-2),
            EndDateTime = DateTime.UtcNow.AddDays(5),
            StartMileage = 30000,
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new CompleteReservationCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new CompleteReservationCommand(1, "Retour OK"), CancellationToken.None);

        result.Status.Should().Be("completed");
        result.EndMileage.Should().Be(30000); // fallback to vehicle.Mileage (no GPS)
        result.ActualKm.Should().Be(0); // 30000 - 30000
        result.Notes.Should().Be("Retour OK");
    }

    [Fact]
    public async Task CompleteReservation_WithCompletedReservation_ThrowsDomainException()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation
        {
            Id = 2,
            VehicleId = 1,
            Status = "completed",
            StartDateTime = DateTime.UtcNow.AddDays(-5),
            EndDateTime = DateTime.UtcNow.AddDays(-1),
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new CompleteReservationCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new CompleteReservationCommand(2, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*en cours*");
    }

    // ── CancelReservation ──

    [Fact]
    public async Task CancelReservation_WithActiveReservation_Cancels()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation
        {
            Id = 3,
            VehicleId = 1,
            Status = "in_progress",
            StartDateTime = DateTime.UtcNow.AddHours(-3),
            EndDateTime = DateTime.UtcNow.AddDays(1),
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new CancelReservationCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new CancelReservationCommand(3, "Plus nécessaire"), CancellationToken.None);

        result.Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task CancelReservation_AlreadyCancelled_ThrowsDomainException()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation
        {
            Id = 4,
            VehicleId = 1,
            Status = "cancelled",
            StartDateTime = DateTime.UtcNow.AddDays(-2),
            EndDateTime = DateTime.UtcNow.AddDays(-1),
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new CancelReservationCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new CancelReservationCommand(4, null), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*terminé ou annulé*");
    }

    // ── SetVehicleRentalStatus ──

    [Fact]
    public async Task SetRentalStatus_MarkAsRented_Succeeds()
    {
        var (context, tenant) = SetupContext();

        var handler = new SetVehicleRentalStatusCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new SetVehicleRentalStatusCommand(1, true), CancellationToken.None);

        result.IsRented.Should().BeTrue();
        result.VehicleName.Should().Be("Peugeot 208");

        var vehicle = await context.Vehicles.FindAsync(1);
        vehicle!.IsRented.Should().BeTrue();
    }

    [Fact]
    public async Task SetRentalStatus_MarkAsRented_WithActiveReservation_ThrowsConflict()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation
        {
            VehicleId = 1,
            Status = "in_progress",
            StartDateTime = DateTime.UtcNow,
            EndDateTime = DateTime.UtcNow.AddDays(1),
            CompanyId = CompanyId
        });
        await context.SaveChangesAsync();

        var handler = new SetVehicleRentalStatusCommandHandler(context, tenant.Object);

        var act = () => handler.Handle(new SetVehicleRentalStatusCommand(1, true), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*emprunt en cours*");
    }

    [Fact]
    public async Task SetRentalStatus_MarkAsAvailable_Succeeds()
    {
        var (context, tenant) = SetupContext();

        var handler = new SetVehicleRentalStatusCommandHandler(context, tenant.Object);

        var result = await handler.Handle(new SetVehicleRentalStatusCommand(3, false), CancellationToken.None);

        result.IsRented.Should().BeFalse();
    }

    // ── GetReservations ──

    [Fact]
    public async Task GetReservations_FiltersByStatus()
    {
        var (context, tenant) = SetupContext();

        context.Reservations.Add(new Reservation { VehicleId = 1, Status = "in_progress", StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddDays(1), CompanyId = CompanyId });
        context.Reservations.Add(new Reservation { VehicleId = 2, Status = "completed", StartDateTime = DateTime.UtcNow.AddDays(-3), EndDateTime = DateTime.UtcNow.AddDays(-1), CompanyId = CompanyId });
        await context.SaveChangesAsync();

        var handler = new GetReservationsQueryHandler(context, tenant.Object);

        var result = await handler.Handle(new GetReservationsQuery(null, null, "in_progress", null, null), CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Status.Should().Be("in_progress");
    }

    // ── GetAvailableVehicles ──

    [Fact]
    public async Task GetAvailableVehicles_ReturnsAllWithCorrectFlags()
    {
        var (context, tenant) = SetupContext();

        // Add an active reservation on vehicle 1
        context.Reservations.Add(new Reservation { VehicleId = 1, Status = "in_progress", StartDateTime = DateTime.UtcNow, EndDateTime = DateTime.UtcNow.AddDays(1), CompanyId = CompanyId });
        await context.SaveChangesAsync();

        var handler = new GetAvailableVehiclesQueryHandler(context, tenant.Object);

        var result = await handler.Handle(new GetAvailableVehiclesQuery(), CancellationToken.None);

        result.Should().HaveCount(3);

        var v1 = result.First(v => v.Id == 1);
        v1.HasActiveReservation.Should().BeTrue();
        v1.IsRented.Should().BeFalse();

        var v2 = result.First(v => v.Id == 2);
        v2.HasActiveReservation.Should().BeFalse();
        v2.IsRented.Should().BeFalse();

        var v3 = result.First(v => v.Id == 3);
        v3.IsRented.Should().BeTrue();
        v3.HasActiveReservation.Should().BeFalse();
    }
}
