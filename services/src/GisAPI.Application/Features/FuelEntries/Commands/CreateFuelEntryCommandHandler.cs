using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public class CreateFuelEntryCommandHandler : IRequestHandler<CreateFuelEntryCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateFuelEntryCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateFuelEntryCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        // Convert date to UTC
        var invoiceDate = request.InvoiceDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc)
            : request.InvoiceDate.ToUniversalTime();

        // Try to find vehicle by plate
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.CompanyId == companyId && 
                (v.Plate == request.VehiclePlate || v.Name == request.VehiclePlate), 
                cancellationToken);

        var entry = new FuelEntry
        {
            CompanyId = companyId,
            VehicleId = vehicle?.Id,
            VehiclePlate = request.VehiclePlate,
            FuelTypeId = request.FuelTypeId,
            Volume = request.Volume,
            PricePerLiter = request.PricePerLiter,
            TotalAmount = request.Volume * request.PricePerLiter,
            InvoiceDate = invoiceDate,
            StationName = request.StationName,
            InvoiceNumber = request.InvoiceNumber,
            Notes = request.Notes,
            DriverId = request.DriverId,
            OdometerKm = request.OdometerKm,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.FuelEntries.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);

        // Notify company admins (must be awaited to avoid DbContext concurrency issues in bulk scenarios)
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (actor != null)
        {
            await _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "cost_created", request.VehiclePlate ?? $"Carburant #{entry.Id}", entry.Id, "cost"
            ), cancellationToken);
        }

        return entry.Id;
    }
}
