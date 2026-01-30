using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentClaims.Queries;

public class GetAccidentClaimStatsQueryHandler : IRequestHandler<GetAccidentClaimStatsQuery, AccidentClaimStatsDto>
{
    private readonly IGisDbContext _context;

    public GetAccidentClaimStatsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<AccidentClaimStatsDto> Handle(GetAccidentClaimStatsQuery request, CancellationToken cancellationToken)
    {
        var claims = await _context.AccidentClaims.ToListAsync(cancellationToken);

        return new AccidentClaimStatsDto(
            claims.Count,
            claims.Count(c => c.Status == "draft"),
            claims.Count(c => c.Status == "submitted"),
            claims.Count(c => c.Status == "under_review"),
            claims.Count(c => c.Status == "approved"),
            claims.Count(c => c.Status == "rejected"),
            claims.Count(c => c.Status == "closed"),
            claims.Sum(c => c.EstimatedDamage),
            claims.Where(c => c.ApprovedAmount.HasValue).Sum(c => c.ApprovedAmount ?? 0)
        );
    }
}



