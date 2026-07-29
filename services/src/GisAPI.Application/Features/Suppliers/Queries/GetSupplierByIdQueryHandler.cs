using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetSupplierByIdQueryHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetSupplierByIdQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SupplierDto?> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant. Le filtre
        // global de multi-tenance (GisDbContext) est contourné pour les administrateurs
        // système — sans ce filtre explicite, un simple Id permettait de consulter la
        // fiche d'un fournisseur d'une AUTRE société (fuite inter-sociétés).
        var companyId = _tenantService.CompanyId ?? 0;

        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.Id && s.CompanyId == companyId, cancellationToken);

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



