using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Roles.Commands.DeleteRole;

public record DeleteRoleCommand(int Id) : ICommand;



