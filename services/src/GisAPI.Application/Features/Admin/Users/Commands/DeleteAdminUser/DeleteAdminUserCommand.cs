using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;

public record DeleteAdminUserCommand(int Id) : ICommand;
