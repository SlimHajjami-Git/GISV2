using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Commands;

public class UpdateSupplierServicesCommandHandler : IRequestHandler<UpdateSupplierServicesCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateSupplierServicesCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateSupplierServicesCommand request, CancellationToken cancellationToken)
    {
        var supplier = await _context.Suppliers
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);

        if (supplier == null)
            return false;

        // Remplacement complet : on supprime les services existants puis on insère la nouvelle liste
        await SupplierServiceCodes.RemplacerAsync(_context, supplier.Id, request.Services, cancellationToken);

        supplier.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}



