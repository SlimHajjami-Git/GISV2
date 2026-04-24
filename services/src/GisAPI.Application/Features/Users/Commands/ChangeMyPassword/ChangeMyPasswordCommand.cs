using MediatR;

namespace GisAPI.Application.Features.Users.Commands.ChangeMyPassword;

/// <summary>
/// Self-service password change: the authenticated user supplies their current
/// password and the new one. Must enforce length (>= 6) and verify the existing hash.
/// </summary>
public record ChangeMyPasswordCommand(
    string CurrentPassword,
    string NewPassword
) : IRequest<Unit>;
