using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public record UpdateDepartmentCommand(
    int Id,
    string Name,
    string? Description,
    bool IsActive
) : ICommand;



