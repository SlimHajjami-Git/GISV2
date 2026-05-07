using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Services;
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

        // Calypso 8 — bug rapporte (page 8 PDF) : "Faux kilometrage" entre
        // emprunt et rapport km (ex: 247 km vs 976 km sur la meme periode
        // pour le meme vehicule). Cause : EndMileage - StartMileage repose
        // sur vehicle.Mileage qui est incremente par Rust avec un floor a
        // 1 km (perd les fractions) et peut diverger du calcul Haversine
        // utilise par le rapport km.
        //
        // Fix : si on a un GPS associe, on calcule ActualKm via la MEME
        // logique Haversine que GetMileagePeriodReport — donc les deux
        // affichages restent coherents pour la meme periode. On retombe sur
        // EndMileage - StartMileage en derniere recourse (vehicule sans GPS
        // ou aucune position dans la fenetre).
        var endTime = DateTime.UtcNow;
        if (reservation.Vehicle?.GpsDeviceId != null && reservation.StartDateTime != default)
        {
            var positions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == reservation.Vehicle.GpsDeviceId.Value
                         && p.RecordedAt >= reservation.StartDateTime
                         && p.RecordedAt <= endTime)
                .OrderBy(p => p.RecordedAt)
                .ToListAsync(ct);

            if (positions.Count >= 2)
            {
                var distanceKm = GpsDistanceCalculator.CalculateTotalDistanceKm(positions);
                reservation.ActualKm = (int)Math.Round(distanceKm);
            }
            else
            {
                reservation.ActualKm = (reservation.StartMileage.HasValue && endMileage.HasValue)
                    ? endMileage.Value - reservation.StartMileage.Value
                    : null;
            }
        }
        else
        {
            reservation.ActualKm = (reservation.StartMileage.HasValue && endMileage.HasValue)
                ? endMileage.Value - reservation.StartMileage.Value
                : null;
        }
        reservation.EndDateTime = endTime;
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
