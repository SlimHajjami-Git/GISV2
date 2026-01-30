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
            .Include(s => s.Services)
            .FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken);

        if (supplier == null)
            return false;

        // Remove all existing services
        _context.SupplierServices.RemoveRange(supplier.Services);

        // Add new valid services
        foreach (var serviceCode in request.Services.Where(s => ValidServices.Contains(s.ToLower())).Distinct())
        {
            _context.SupplierServices.Add(new SupplierService
            {
                SupplierId = supplier.Id,
                ServiceCode = serviceCode.ToLower()
            });
        }

        supplier.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        
        return true;
    }
}



