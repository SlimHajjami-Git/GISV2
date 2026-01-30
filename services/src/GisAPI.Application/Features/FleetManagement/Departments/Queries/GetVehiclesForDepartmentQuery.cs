using MediatR;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public record GetVehiclesForDepartmentQuery(int DepartmentId) : IRequest<List<VehicleForAssignmentDto>>;

public record VehicleForAssignmentDto(
    int Id,
    string Name,
    string? Plate,
    string? Type,
    string? Brand,
    string? Model,
    bool IsAssigned,
    int? CurrentDepartmentId,
    string? CurrentDepartmentName
);



