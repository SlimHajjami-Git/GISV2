using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.SyncMileage;

public class SyncMileageCommandHandler : IRequestHandler<SyncMileageCommand, SyncMileageResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SyncMileageCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SyncMileageResult> Handle(SyncMileageCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct);

        if (vehicle == null)
            throw new NotFoundException("Vehicle", request.VehicleId);

        var previousMileage = vehicle.Mileage;
        var source = "manual";
        var updated = false;

        // Reset corrupted mileage (protocol artifacts or absurd values)
        var currentMileage = vehicle.Mileage;
        if (currentMileage >= 1_048_000 && currentMileage <= 1_049_000) currentMileage = 0;
        else if (currentMileage > 1_000_000) currentMileage = 0;

        // Si le véhicule a un GPS, chercher le dernier odometer_km
        if (vehicle.GpsDeviceId.HasValue)
        {
            var lastOdometer = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value 
                         && p.OdometerKm.HasValue 
                         && p.OdometerKm > 0)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);

            if (lastOdometer.HasValue)
            {
                var odoValue = lastOdometer.Value;

                // Skip GPS protocol artifact (~2^20 = 1,048,576 default)
                if (odoValue >= 1_048_000 && odoValue <= 1_049_000)
                {
                    // Protocol default, ignore — but still reset corrupted mileage
                    if (vehicle.Mileage != currentMileage)
                    {
                        vehicle.Mileage = currentMileage;
                        vehicle.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        updated = true;
                    }
                }
                else
                {
                    // Values > 1,000,000 are likely in meters from GPS tracker
                    if (odoValue > 1_000_000) odoValue = odoValue / 1000;

                    // Accept if valid and greater than (sanitized) current mileage
                    if (odoValue > 0 && odoValue <= 500_000 && odoValue > currentMileage)
                    {
                        vehicle.Mileage = (int)odoValue;
                        vehicle.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        source = "gps";
                        updated = true;
                    }
                    else if (vehicle.Mileage != currentMileage)
                    {
                        // Reset corrupted value even if GPS odometer isn't better
                        vehicle.Mileage = currentMileage;
                        vehicle.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        updated = true;
                    }
                }
            }
            else if (vehicle.Mileage != currentMileage)
            {
                // No GPS odometer, but mileage was corrupted — reset it
                vehicle.Mileage = currentMileage;
                vehicle.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                updated = true;
            }
        }

        return new SyncMileageResult(
            vehicle.Id,
            previousMileage,
            vehicle.Mileage,
            source,
            updated
        );
    }
}
