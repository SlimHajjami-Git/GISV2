using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Commands;

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateSupplierCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        var supplier = new Supplier
        {
            Name = request.Name,
            Type = request.Type,
            Address = request.Address,
            City = request.City,
            PostalCode = request.PostalCode,
            ContactName = request.ContactName,
            Phone = request.Phone,
            Email = request.Email,
            Website = request.Website,
            TaxId = request.TaxId,
            BankAccount = request.BankAccount,
            PaymentTerms = request.PaymentTerms ?? "net30",
            DiscountPercent = request.DiscountPercent,
            Rating = request.Rating ?? 0,
            Notes = request.Notes,
            IsActive = request.IsActive,
            CompanyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Suppliers.Add(supplier);
        await _context.SaveChangesAsync(cancellationToken);

        // Add services if provided
        if (request.Services != null && request.Services.Any())
        {
            var validServices = new[] { "mecanique", "carrosserie", "electricite", "pneumatique", "vidange", "climatisation", "diagnostic" };
            
            foreach (var serviceCode in request.Services.Where(s => validServices.Contains(s.ToLower())))
            {
                _context.SupplierServices.Add(new SupplierService
                {
                    SupplierId = supplier.Id,
                    ServiceCode = serviceCode.ToLower()
                });
            }
            await _context.SaveChangesAsync(cancellationToken);
        }

        // Notify company admins
        var companyId = _tenantService.CompanyId ?? 0;
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (actor != null && companyId > 0)
        {
            _ = _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "supplier_created", supplier.Name, supplier.Id, "supplier"
            ), cancellationToken);
        }

        return supplier.Id;
    }
}



