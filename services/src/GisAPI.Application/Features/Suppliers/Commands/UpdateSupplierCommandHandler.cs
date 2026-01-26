using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Commands;

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateSupplierCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        var supplier = await _context.Suppliers
            .Include(s => s.Services)
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken);

        if (supplier == null)
            return false;

        // Update only provided fields
        if (request.Name != null) supplier.Name = request.Name;
        if (request.Type != null) supplier.Type = request.Type;
        if (request.Address != null) supplier.Address = request.Address;
        if (request.City != null) supplier.City = request.City;
        if (request.PostalCode != null) supplier.PostalCode = request.PostalCode;
        if (request.ContactName != null) supplier.ContactName = request.ContactName;
        if (request.Phone != null) supplier.Phone = request.Phone;
        if (request.Email != null) supplier.Email = request.Email;
        if (request.Website != null) supplier.Website = request.Website;
        if (request.TaxId != null) supplier.TaxId = request.TaxId;
        if (request.BankAccount != null) supplier.BankAccount = request.BankAccount;
        if (request.PaymentTerms != null) supplier.PaymentTerms = request.PaymentTerms;
        if (request.DiscountPercent.HasValue) supplier.DiscountPercent = request.DiscountPercent;
        if (request.Rating.HasValue) supplier.Rating = Math.Clamp(request.Rating.Value, 0, 5);
        if (request.Notes != null) supplier.Notes = request.Notes;
        if (request.IsActive.HasValue) supplier.IsActive = request.IsActive.Value;

        supplier.UpdatedAt = DateTime.UtcNow;

        // Update services if provided
        if (request.Services != null)
        {
            var validServices = new[] { "mecanique", "carrosserie", "electricite", "pneumatique", "vidange", "climatisation", "diagnostic" };
            
            // Remove existing services
            _context.SupplierServices.RemoveRange(supplier.Services);
            
            // Add new services
            foreach (var serviceCode in request.Services.Where(s => validServices.Contains(s.ToLower())))
            {
                _context.SupplierServices.Add(new SupplierService
                {
                    SupplierId = supplier.Id,
                    ServiceCode = serviceCode.ToLower()
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
