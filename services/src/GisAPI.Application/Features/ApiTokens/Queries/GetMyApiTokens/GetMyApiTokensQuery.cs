using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.ApiTokens.Queries.GetMyApiTokens;

public record GetMyApiTokensQuery : IQuery<List<ApiTokenDto>>;

/// <summary>Un jeton de l'utilisateur, SANS son secret (seule l'empreinte existe en base).</summary>
/// <param name="Status">pending (utilisable), used (consommé) ou expired.</param>
public record ApiTokenDto(
    int Id,
    string TokenPrefix,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string Status);
