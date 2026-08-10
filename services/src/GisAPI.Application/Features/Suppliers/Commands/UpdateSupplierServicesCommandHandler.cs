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

        // Remplacement complet : on supprime les services existants puis on insère la nouvelle liste
        var existingServices = await _context.SupplierServices
            .Where(ss => ss.SupplierId == supplier.Id)
            .ToListAsync(cancellationToken);
        _context.SupplierServices.RemoveRange(existingServices);

        foreach (var serviceCode in request.Services
                     .Select(s => s.ToLower())
                     .Where(s => ValidServices.Contains(s))
                     .Distinct())
        {
            _context.SupplierServices.Add(new SupplierService
            {
                SupplierId = supplier.Id,
                ServiceCode = serviceCode
            });
        }

        supplier.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}



