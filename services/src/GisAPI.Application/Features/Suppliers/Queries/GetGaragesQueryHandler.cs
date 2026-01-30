using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetGaragesQueryHandler : IRequestHandler<GetGaragesQuery, PaginatedList<SupplierDto>>
{
    private readonly IGisDbContext _context;

    public GetGaragesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<PaginatedList<SupplierDto>> Handle(GetGaragesQuery request, CancellationToken cancellationToken)
    {
        var query = _context.Suppliers
            .Include(s => s.Services)
            .Where(s => s.Type == "garage")
            .AsQueryable();

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

        // Order by rating desc, then name
        query = query.OrderByDescending(s => s.Rating).ThenBy(s => s.Name);

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
                s.PostalCode,
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
                s.Services.Select(svc => svc.ServiceCode).ToList(),
                s.CreatedAt,
                s.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedList<SupplierDto>(items, totalCount, request.Page, request.PageSize);
    }
}



