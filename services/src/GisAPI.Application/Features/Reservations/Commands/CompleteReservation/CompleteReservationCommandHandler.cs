using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Commands.CompleteReservation;

public class CompleteReservationCommandHandler : IRequestHandler<CompleteReservationCommand, ReservationDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CompleteReservationCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ReservationDto> Handle(CompleteReservationCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var reservation = await _context.Reservations
            .Include(r => r.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Include(r => r.RequestedByUser)
            .Include(r => r.AssignedDriver)
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId && r.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Reservation", request.ReservationId);

        if (reservation.Status != "in_progress")
            throw new DomainException("Seuls les emprunts en cours peuvent être retournés");

        // Capture GPS mileage at return
        int? endMileage = null;
        if (reservation.Vehicle?.GpsDeviceId != null)
        {
            var lastOdometer = await _context.GpsPositions
                .Where(p => p.DeviceId == reservation.Vehicle.GpsDeviceId.Value
                         && p.OdometerKm.HasValue
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);

            if (lastOdometer.HasValue && lastOdometer.Value > 0)
                endMileage = (int)lastOdometer.Value;
        }

        endMileage ??= reservation.Vehicle?.Mileage;

        reservation.EndMileage = endMileage;
        reservation.ActualKm = (reservation.StartMileage.HasValue && endMileage.HasValue)
            ? endMileage.Value - reservation.StartMileage.Value
            : null;
        reservation.EndDateTime = DateTime.UtcNow;
        reservation.Status = "completed";
        if (request.Notes != null)
            reservation.Notes = request.Notes;
        reservation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new ReservationDto(
            reservation.Id,
            reservation.VehicleId,
            reservation.Vehicle?.Name ?? "",
            reservation.Vehicle?.Plate,
            reservation.RequestedByUserId,
            reservation.RequestedByUser?.FullName,
            reservation.AssignedDriverId,
            reservation.AssignedDriver?.FullName,
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
