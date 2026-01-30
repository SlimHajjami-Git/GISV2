using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public record GetDepartmentByIdQuery(int Id) : IQuery<DepartmentDetailDto?>;

public record DepartmentDetailDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    List<DepartmentVehicleDto> Vehicles,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record DepartmentVehicleDto(
    int Id,
    string Name,
    string? Plate,
    string? Type,
    string Status
);



