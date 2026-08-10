using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetGaragesQueryHandler : IRequestHandler<GetGaragesQuery, PaginatedList<SupplierDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetGaragesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<SupplierDto>> Handle(GetGaragesQuery request, CancellationToken cancellationToken)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant. Le filtre
        // global de multi-tenance (GisDbContext) est contourné pour les administrateurs
        // système — sans ce filtre explicite, la liste « Garages » affichait les garages
        // de TOUTES les sociétés (fuite inter-sociétés).
        var companyId = _tenantService.CompanyId ?? 0;

        var query = _context.Suppliers
            .Where(s => s.CompanyId == companyId && s.Type == "garage");

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(s => 
                s.Name.ToLower().Contains(term) ||
                (s.City != null && s.City.ToLower().Contains(term)) ||
                (s.ContactName != null && s.ContactName.ToLower().Contains(term)));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == request.IsActive.Value);
        }

        // Order by rating desc, then name. Cast en double : SQLite (tests) ne sait pas
        // trier un decimal ; l'ordre est identique sous PostgreSQL.
        query = query.OrderByDescending(s => (double)s.Rating).ThenBy(s => s.Name);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.Name,
                s.Type,
                s.Address,
                s.City,
                null,
                s.ContactName,
                s.Phone,
                s.Email,
                s.Website,
                s.TaxId,
                s.BankAccount,
                s.PaymentTerms,
                s.DiscountPercent,
                s.Rating,
                s.Notes,
                s.IsActive,
                new List<string>(),
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedList<SupplierDto>(items, totalCount, request.Page, request.PageSize);
    }
}



