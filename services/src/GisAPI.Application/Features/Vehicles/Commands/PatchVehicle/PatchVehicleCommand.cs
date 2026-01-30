using MediatR;

namespace GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;

public record PatchVehicleCommand(
    int Id,
    int? SpeedLimit,
    int? DepartmentId,
    string? FuelType
) : IRequest<Unit>;



