using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetRenewalHistoryQueryHandler : IRequestHandler<GetRenewalHistoryQuery, List<RenewalHistoryDto>>
{
    private readonly IGisDbContext _context;
    private static readonly string[] DocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit" };

    public GetRenewalHistoryQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<RenewalHistoryDto>> Handle(GetRenewalHistoryQuery request, CancellationToken cancellationToken)
    {
        var costs = await _context.VehicleCosts
            .Where(c => c.VehicleId == request.VehicleId && DocumentTypes.Contains(c.Type))
            .OrderByDescending(c => c.Date)
            .ToListAsync(cancellationToken);

        return costs.Select(c => new RenewalHistoryDto(
            c.Id,
            c.Type,
            c.Amount,
            c.Date,
            c.ExpiryDate,
            c.DocumentNumber,
            c.Description,
            null,
            c.DocumentUrl
        )).ToList();
    }
}
