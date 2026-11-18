using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.ApiKeys.Queries.GetApiKeys;

/// <summary>Les clés d'automatisation de la société (migration 069), pour son administrateur.</summary>
public record GetApiKeysQuery : IQuery<ApiKeyListDto>;

/// <summary>La liste et les règles affichées à côté (plafonds, durée de vie).</summary>
public record ApiKeyListDto(
    List<ApiKeyDto> Keys,
    int MaxActive,
    int MaxExchangesPer24Hours,
    int LifetimeDays);

/// <summary>Une clé, SANS son secret (seule l'empreinte existe en base).</summary>
/// <param name="Status">active, revoked ou expired.</param>
/// <param name="Exchanges24Hours">Jetons obtenus avec cette clé sur les dernières 24 heures.</param>
public record ApiKeyDto(
    int Id,
    string Label,
    string KeyPrefix,
    string? CreatedBy,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    DateTime? LastUsedAt,
    string? LastUsedIp,
    int Exchanges24Hours,
    string Status);

public class GetApiKeysQueryHandler : IRequestHandler<GetApiKeysQuery, ApiKeyListDto>
{
    public const int MaxRows = 20;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetApiKeysQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<ApiKeyListDto> Handle(GetApiKeysQuery request, CancellationToken ct)
    {
        // Contrairement aux jetons (délégation personnelle), une clé est un objet de la SOCIÉTÉ :
        // tout administrateur la voit et peut la révoquer, même si son créateur est parti.
        var (_, companyId) = await ApiKeyAdmin.RequireAsync(_context, _tenant, ct);
        var now = DateTime.UtcNow;

        var keys = await _context.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .Where(k => k.CompanyId == companyId)
            .OrderByDescending(k => k.CreatedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        var keyIds = keys.Select(k => k.Id).ToList();
        var since = now.AddHours(-24);
        var exchanges = await _context.ApiTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.ApiKeyId != null && keyIds.Contains(t.ApiKeyId.Value) && t.CreatedAt > since)
            .GroupBy(t => t.ApiKeyId!.Value)
            .Select(g => new { KeyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.KeyId, x => x.Count, ct);

        var ownerIds = keys.Select(k => k.UserId).Distinct().ToList();
        var owners = (await _context.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => ownerIds.Contains(u.Id) && u.CompanyId == companyId)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(ct))
            .ToDictionary(u => u.Id, u =>
            {
                var name = $"{u.FirstName} {u.LastName}".Trim();
                return string.IsNullOrEmpty(name) ? u.Email : name;
            });

        var rows = keys.Select(k => new ApiKeyDto(
                k.Id, k.Label, k.KeyPrefix,
                owners.TryGetValue(k.UserId, out var owner) ? owner : null,
                k.CreatedAt, k.ExpiresAt, k.RevokedAt, k.LastUsedAt, k.LastUsedIp,
                exchanges.TryGetValue(k.Id, out var count) ? count : 0,
                ApiKeyAccess.Status(k, now)))
            .ToList();

        return new ApiKeyListDto(rows, ApiKeyAccess.MaxActivePerCompany,
            ApiKeyAccess.MaxExchangesPer24Hours, ApiKeyAccess.LifetimeDays);
    }
}
