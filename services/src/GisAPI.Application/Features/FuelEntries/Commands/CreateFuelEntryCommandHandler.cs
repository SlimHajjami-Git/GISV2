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

        // Calypso 9 p2 — operator may submit a ticket where only the
        // gross total is legible. Honour an explicit TotalAmount > 0
        // and fall back to volume × price otherwise. Volume / price
        // are still persisted as-is (may be 0) so the per-litre stats
        // simply skip rows with missing breakdown.
        var totalAmount = request.TotalAmount.HasValue && request.TotalAmount.Value > 0
            ? request.TotalAmount.Value
            : request.Volume * request.PricePerLiter;

        var entry = new FuelEntry
        {
            CompanyId = companyId,
            VehicleId = vehicle?.Id,
            VehiclePlate = request.VehiclePlate,
            FuelTypeId = request.FuelTypeId,
            Volume = request.Volume,
            PricePerLiter = request.PricePerLiter,
            TotalAmount = totalAmount,
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
            var fuelLabel = $"{request.Volume:0.#}L — {request.VehiclePlate ?? vehicle?.Name ?? "Véhicule"}";
            await _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "fuel_created", fuelLabel, entry.Id, "fuel_entry"
            ), cancellationToken);
        }

        return entry.Id;
    }
}
