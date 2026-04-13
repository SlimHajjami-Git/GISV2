using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Commands.CancelReservation;

public class CancelReservationCommandHandler : IRequestHandler<CancelReservationCommand, ReservationDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CancelReservationCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ReservationDto> Handle(CancelReservationCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var reservation = await _context.Reservations
            .Include(r => r.Vehicle)
            .Include(r => r.RequestedByUser)
            .Include(r => r.AssignedDriver)
            .FirstOrDefaultAsync(r => r.Id == request.ReservationId && r.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Reservation", request.ReservationId);

        if (reservation.Status == "completed" || reservation.Status == "cancelled")
            throw new DomainException("Cet emprunt est déjà terminé ou annulé");

        reservation.Status = "cancelled";
        reservation.RejectionReason = request.Reason;
        reservation.EndDateTime = DateTime.UtcNow;
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
