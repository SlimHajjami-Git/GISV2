using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, PaginatedList<SupplierDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetSuppliersQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant. Le filtre
        // global de multi-tenance (GisDbContext) est contourné pour les administrateurs
        // système — sans ce filtre explicite, la liste « Fournisseurs » affichait les
        // fournisseurs de TOUTES les sociétés (fuite inter-sociétés).
        var companyId = _tenantService.CompanyId ?? 0;

        var query = _context.Suppliers
            .Where(s => s.CompanyId == companyId);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(s => 
                s.Name.ToLower().Contains(term) ||
                (s.City != null && s.City.ToLower().Contains(term)) ||
                (s.ContactName != null && s.ContactName.ToLower().Contains(term)) ||
                (s.Phone != null && s.Phone.Contains(term)) ||
                (s.Email != null && s.Email.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            query = query.Where(s => s.Type == request.Type);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == request.IsActive.Value);
        }

        // Order by name
        query = query.OrderBy(s => s.Name);

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
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



