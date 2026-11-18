using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.ApiKeys.Commands.CreateApiKey;

/// <param name="Label">Nom de la clé (« serveur de paie ») ; un nom par défaut s'il est vide.</param>
/// <param name="Email">L'e-mail du compte connecté, ressaisi.</param>
/// <param name="Password">Son mot de passe, ressaisi.</param>
/// <param name="IpAddress">Adresse de l'appelant, conservée pour l'audit.</param>
public record CreateApiKeyCommand(string? Label, string Email, string Password, string? IpAddress = null)
    : ICommand<ApiKeyCreatedDto>;

/// <summary>Seule réponse qui contient la clé en clair : elle n'est plus récupérable ensuite.</summary>
public record ApiKeyCreatedDto(int Id, string Key, string KeyPrefix, string Label, DateTime ExpiresAt);
