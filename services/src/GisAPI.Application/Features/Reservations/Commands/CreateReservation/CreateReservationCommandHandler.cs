using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Commands.CreateReservation;

public class CreateReservationCommandHandler : IRequestHandler<CreateReservationCommand, ReservationDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateReservationCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ReservationDto> Handle(CreateReservationCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var company = await _context.Societes
            .FirstOrDefaultAsync(s => s.Id == companyId, ct)
            ?? throw new NotFoundException("Societe", companyId);

        if (company.Type != "location")
            throw new DomainException("Cette fonctionnalité est réservée aux sociétés de type location");

        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        if (vehicle.IsRented)
            throw new ConflictException("Ce véhicule est actuellement loué et ne peut pas être emprunté");

        var hasActiveReservation = await _context.Reservations
            .AnyAsync(r => r.VehicleId == request.VehicleId && r.Status == "in_progress", ct);

        if (hasActiveReservation)
            throw new ConflictException("Ce véhicule a déjà un emprunt en cours");

        if (request.AssignedDriverId.HasValue)
        {
            var driver = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == request.AssignedDriverId.Value && u.CompanyId == companyId, ct)
                ?? throw new NotFoundException("User", request.AssignedDriverId.Value);
        }

        // Capture GPS mileage (same pattern as SyncMileageCommandHandler)
        int? startMileage = null;
        if (vehicle.GpsDeviceId.HasValue)
        {
            var lastOdometer = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value
                         && p.OdometerKm.HasValue
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);

            if (lastOdometer.HasValue && lastOdometer.Value > 0)
                startMileage = (int)lastOdometer.Value;
        }

        startMileage ??= vehicle.Mileage;

        var reservation = new Reservation
        {
            VehicleId = request.VehicleId,
            RequestedByUserId = _tenantService.UserId,
            AssignedDriverId = request.AssignedDriverId,
            Purpose = request.Purpose,
            Destination = request.Destination,
            StartDateTime = DateTime.UtcNow,
            EndDateTime = DateTime.UtcNow.AddDays(30), // default, updated on return
            EstimatedKm = request.EstimatedKm,
            StartMileage = startMileage,
            Status = "in_progress",
            Notes = request.Notes,
            CompanyId = companyId
        };

        _context.Reservations.Add(reservation);
        await _context.SaveChangesAsync(ct);

        var requestedByUser = _tenantService.UserId.HasValue
            ? await _context.Users.FirstOrDefaultAsync(u => u.Id == _tenantService.UserId.Value, ct)
            : null;

        var assignedDriver = request.AssignedDriverId.HasValue
            ? await _context.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedDriverId.Value, ct)
            : null;

        return new ReservationDto(
            reservation.Id,
            vehicle.Id,
            vehicle.Name,
            vehicle.Plate,
            reservation.RequestedByUserId,
            requestedByUser?.FullName,
            reservation.AssignedDriverId,
            assignedDriver?.FullName,
            reservation.Purpose,
            reservation.Destination,
            reservation.StartDateTime,
            reservation.EndDateTime,
            reservation.EstimatedKm,
            reservation.ActualKm,
            reservation.StartMileage,
            reservation.EndMileage,
            reservation.Status,
            reservation.Notes,
            reservation.CreatedAt
        );
    }
}
