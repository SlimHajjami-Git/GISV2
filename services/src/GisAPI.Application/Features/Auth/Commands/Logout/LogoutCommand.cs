using MediatR;

namespace GisAPI.Application.Features.Auth.Commands.Logout;

/// <summary>
/// Records a user logout in the audit trail and revokes the user's active refresh tokens.
/// Best-effort: failures are swallowed so logout never errors for the client.
/// </summary>
public record LogoutCommand(int UserId, int CompanyId, string? IpAddress = null, string? UserAgent = null)
    : IRequest<Unit>;
