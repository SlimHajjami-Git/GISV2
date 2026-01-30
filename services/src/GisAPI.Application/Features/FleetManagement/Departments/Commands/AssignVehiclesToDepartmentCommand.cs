using MediatR;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public record AssignVehiclesToDepartmentCommand(
    int DepartmentId,
    List<int> VehicleIds
) : IRequest<Unit>;



