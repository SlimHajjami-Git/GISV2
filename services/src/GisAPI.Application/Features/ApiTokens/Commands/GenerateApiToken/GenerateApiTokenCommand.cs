using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.ApiTokens.Commands.GenerateApiToken;

/// <param name="Email">L'e-mail du compte connecté, ressaisi.</param>
/// <param name="Password">Son mot de passe, ressaisi.</param>
/// <param name="IpAddress">Adresse de l'appelant, conservée pour l'audit.</param>
public record GenerateApiTokenCommand(string Email, string Password, string? IpAddress = null)
    : ICommand<ApiTokenCreatedDto>;

/// <summary>Seule réponse qui contient le jeton en clair : il n'est plus récupérable ensuite.</summary>
public record ApiTokenCreatedDto(
    int Id,
    string Token,
    string TokenPrefix,
    DateTime ExpiresAt,
    int ExpiresInSeconds);
