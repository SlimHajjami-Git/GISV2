using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public record GetDepartmentsQuery(
    string? SearchTerm = null,
    bool? IsActive = null
) : IQuery<List<DepartmentDto>>;

public record DepartmentDto(
    int Id,
    string Name,
    string? Description,
    bool IsActive,
    int VehicleCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);



