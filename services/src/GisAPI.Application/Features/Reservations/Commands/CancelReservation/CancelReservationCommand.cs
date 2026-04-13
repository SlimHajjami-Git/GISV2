using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Commands.CancelReservation;

public record CancelReservationCommand(int ReservationId, string? Reason) : ICommand<ReservationDto>;
