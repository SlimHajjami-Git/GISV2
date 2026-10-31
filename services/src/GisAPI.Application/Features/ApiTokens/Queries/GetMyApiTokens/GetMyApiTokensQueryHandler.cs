using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.ApiTokens.Queries.GetMyApiTokens;

public class GetMyApiTokensQueryHandler : IRequestHandler<GetMyApiTokensQuery, List<ApiTokenDto>>
{
    public const int MaxRows = 10;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetMyApiTokensQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<List<ApiTokenDto>> Handle(GetMyApiTokensQuery request, CancellationToken ct)
    {
        var userId = _tenant.UserId ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société non identifiée");
        var now = DateTime.UtcNow;

        // Les jetons de l'APPELANT seulement, même pour un administrateur de société : un jeton
        // est une délégation personnelle de droits, pas un objet de gestion.
        var rows = await _context.ApiTokens.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.UserId == userId && t.CompanyId == companyId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(MaxRows)
            .ToListAsync(ct);

        return rows.Select(t => new ApiTokenDto(
                t.Id, t.TokenPrefix, t.CreatedAt, t.ExpiresAt, t.UsedAt,
                t.UsedAt != null ? "used" : t.ExpiresAt <= now ? "expired" : "pending"))
            .ToList();
    }
}
