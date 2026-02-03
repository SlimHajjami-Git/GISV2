using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Commands;

public class UpdateSupplierServicesCommandHandler : IRequestHandler<UpdateSupplierServicesCommand, bool>
{
    private readonly IGisDbContext _context;
    private static readonly string[] ValidServices = { "mecanique", "carrosserie", "electricite", "pneumatique", "vidange", "climatisation", "diagnostic" };

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

        // Services table doesn't exist yet - just update timestamp
        supplier.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        
        return true;
    }
}



