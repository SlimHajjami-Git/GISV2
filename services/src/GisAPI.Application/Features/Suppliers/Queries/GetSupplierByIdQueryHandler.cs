using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetSupplierByIdQueryHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto?>
{
    private readonly IGisDbContext _context;

    public GetSupplierByIdQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SupplierDto?> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (supplier == null)
            return null;

        return new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.Type,
            supplier.Address,
            supplier.City,
            null,
            supplier.ContactName,
            supplier.Phone,
            supplier.Email,
            supplier.Website,
            supplier.TaxId,
            supplier.BankAccount,
            supplier.PaymentTerms,
            supplier.DiscountPercent,
            supplier.Rating,
            supplier.Notes,
            supplier.IsActive,
            new List<string>(),
            supplier.CreatedAt,
            supplier.UpdatedAt
        );
    }
}



