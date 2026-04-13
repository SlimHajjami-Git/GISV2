using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Commands.CompleteReservation;

public record CompleteReservationCommand(int ReservationId, string? Notes) : ICommand<ReservationDto>;
