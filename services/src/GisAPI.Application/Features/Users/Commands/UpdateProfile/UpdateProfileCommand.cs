using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Commands.UpdateProfile;

/// <summary>
/// Self-service profile update: any authenticated user can update their own
/// basic contact info (name, email, phone). No permission / role changes here —
/// that lives on UpdateUserCommand which admins use.
/// </summary>
public record UpdateProfileCommand(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    bool DailyReportEmailEnabled = false
) : ICommand<UserListDto>;
