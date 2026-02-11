namespace GisAPI.DTOs;

// Auth DTOs are now in GisAPI.Application.Features.Auth.Commands.Login namespace
// (LoginResponse, UserDto, SubscriptionFeaturesDto)
// Request DTOs (LoginRequest, RegisterRequest) are in AuthController.cs

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);
