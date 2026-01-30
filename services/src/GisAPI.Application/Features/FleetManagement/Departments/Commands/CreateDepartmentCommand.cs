using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public record CreateDepartmentCommand(
    string Name,
    string? Description,
    bool IsActive = true
) : ICommand<int>;



