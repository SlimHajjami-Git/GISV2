using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.ResetAdminUserPassword;

public record ResetAdminUserPasswordCommand(int UserId, string NewPassword) : ICommand;
