using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.ApiTokens.Queries.GetMyApiTokens;

public record GetMyApiTokensQuery : IQuery<List<ApiTokenDto>>;

/// <summary>
/// L'accès API est-il ouvert à l'utilisateur connecté ? Le profil n'affiche « Accès API » que
/// dans ce cas (administrateur de la société, société activée par Belive, migration 065).
/// </summary>
public record GetMyApiAccessQuery : IQuery<ApiAccessDto>;

/// <param name="Allowed">vrai si ce compte peut générer des jetons.</param>
/// <param name="Reason">sinon, la raison, en français.</param>
public record ApiAccessDto(bool Allowed, string? Reason);

/// <summary>Un jeton de l'utilisateur, SANS son secret (seule l'empreinte existe en base).</summary>
/// <param name="Status">pending (utilisable), used (consommé) ou expired.</param>
public record ApiTokenDto(
    int Id,
    string TokenPrefix,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? UsedAt,
    string Status);
