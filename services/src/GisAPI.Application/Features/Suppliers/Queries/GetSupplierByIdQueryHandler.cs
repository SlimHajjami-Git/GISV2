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

        // Les services étaient renvoyés vides en dur (DEF-012) ; GET /{id}/services
        // s'appuie aussi sur cette requête.
        var services = await SupplierServiceCodes.ParFournisseurAsync(
            _context, new[] { supplier.Id }, cancellationToken);

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
            services.GetValueOrDefault(supplier.Id) ?? new List<string>(),
            supplier.CreatedAt,
            supplier.UpdatedAt
        );
    }
}



