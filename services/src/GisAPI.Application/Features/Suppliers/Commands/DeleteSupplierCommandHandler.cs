using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Commands;

public class DeleteSupplierCommandHandler : IRequestHandler<DeleteSupplierCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteSupplierCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        var supplier = await _context.Suppliers
            .Include(s => s.Services)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (supplier == null)
            return false;

        // Remove associated services first (cascade should handle this, but explicit is safer)
        _context.SupplierServices.RemoveRange(supplier.Services);
        
        // Remove supplier
        _context.Suppliers.Remove(supplier);
        
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
