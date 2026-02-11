using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Commands.DeleteUser;

public record DeleteUserCommand(int Id) : ICommand;
