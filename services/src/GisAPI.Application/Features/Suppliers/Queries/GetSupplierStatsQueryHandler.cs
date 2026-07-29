using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetSupplierStatsQueryHandler : IRequestHandler<GetSupplierStatsQuery, SupplierStatsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetSupplierStatsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SupplierStatsDto> Handle(GetSupplierStatsQuery request, CancellationToken cancellationToken)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant. Le filtre
        // global de multi-tenance (GisDbContext) est contourné pour les administrateurs
        // système — sans ce filtre explicite, les statistiques fournisseurs agrégeaient
        // TOUTES les sociétés (fuite inter-sociétés).
        var companyId = _tenantService.CompanyId ?? 0;

        var suppliers = await _context.Suppliers
            .Where(s => s.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var total = suppliers.Count;
        var active = suppliers.Count(s => s.IsActive);
        var inactive = total - active;
        var avgRating = suppliers.Any(s => s.Rating > 0) 
            ? suppliers.Where(s => s.Rating > 0).Average(s => s.Rating) 
            : 0m;

        var byType = suppliers
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SupplierStatsDto(
            total,
            active,
            inactive,
            Math.Round(avgRating, 1),
            byType
        );
    }
}



