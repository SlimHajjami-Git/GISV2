using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Queries.GetReservations;

public record GetReservationsQuery(
    int? VehicleId,
    int? DriverId,
    string? Status,
    DateTime? From,
    DateTime? To
) : ICommand<List<ReservationDto>>;
