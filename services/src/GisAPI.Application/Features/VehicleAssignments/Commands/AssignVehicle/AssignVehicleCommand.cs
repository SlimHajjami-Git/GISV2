using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleAssignments.Commands.AssignVehicle;

public record AssignVehicleCommand(
    int VehicleId,
    int UserId,
    string? Notes
) : ICommand<VehicleAssignmentDto>;

public record VehicleAssignmentDto(
    int Id,
    int VehicleId,
    string VehicleName,
    int UserId,
    string UserName,
    DateTime AssignedAt,
    bool IsActive
);



