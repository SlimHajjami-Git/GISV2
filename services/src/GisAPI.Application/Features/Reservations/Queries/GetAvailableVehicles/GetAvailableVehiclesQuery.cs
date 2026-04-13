using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Queries.GetAvailableVehicles;

public record GetAvailableVehiclesQuery() : ICommand<List<AvailableVehicleDto>>;
