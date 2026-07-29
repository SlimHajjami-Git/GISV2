using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Documents.Queries;

public class GetRenewalHistoryQueryHandler : IRequestHandler<GetRenewalHistoryQuery, List<RenewalHistoryDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly string[] DocumentTypes = { "insurance", "technical_inspection", "tax", "registration", "transport_permit" };

    public GetRenewalHistoryQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<RenewalHistoryDto>> Handle(GetRenewalHistoryQuery request, CancellationToken cancellationToken)
    {
        // Écran opérationnel : le vehicleId vient de l'URL sans contrôle
        // d'appartenance. Le filtre global multi-tenance étant contourné pour les
        // administrateurs système, on borne explicitement à leur société.
        var companyId = _tenantService.CompanyId ?? 0;

        var costs = await _context.VehicleCosts
            .Where(c => c.CompanyId == companyId && c.VehicleId == request.VehicleId && DocumentTypes.Contains(c.Type))
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



