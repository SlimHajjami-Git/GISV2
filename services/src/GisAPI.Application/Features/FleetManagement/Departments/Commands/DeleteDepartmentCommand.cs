using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public record DeleteDepartmentCommand(int Id) : ICommand;



