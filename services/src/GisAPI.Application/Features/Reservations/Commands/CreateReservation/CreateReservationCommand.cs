using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Commands.CreateReservation;

public record CreateReservationCommand(
    int VehicleId,
    int? AssignedDriverId,
    string? Purpose,
    string? Destination,
    int? EstimatedKm,
    string? Notes
) : ICommand<ReservationDto>;
