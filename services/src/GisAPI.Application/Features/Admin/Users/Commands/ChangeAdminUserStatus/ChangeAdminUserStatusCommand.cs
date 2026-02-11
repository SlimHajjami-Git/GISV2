using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.ChangeAdminUserStatus;

public record ChangeAdminUserStatusCommand(int UserId, string Status) : ICommand;
