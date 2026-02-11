using GisAPI.Application.Features.Auth.Commands.Login;
using MediatR;

namespace GisAPI.Application.Features.Auth.Commands.RefreshToken;

public record RefreshTokenCommand(string Token, string RefreshToken) : IRequest<LoginResponse>;
