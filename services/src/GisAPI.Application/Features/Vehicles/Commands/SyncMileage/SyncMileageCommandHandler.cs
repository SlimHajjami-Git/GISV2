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

        // Si le véhicule a un GPS, chercher le dernier odometer_km
        if (vehicle.GpsDeviceId.HasValue)
        {
            var firmwareVersion = vehicle.GpsDevice?.FirmwareVersion;
            var isFirmwareL = !string.IsNullOrEmpty(firmwareVersion)
                && firmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase);

            var lastOdometer = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value 
                         && p.OdometerKm.HasValue 
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);

            if (lastOdometer.HasValue && lastOdometer.Value > 0)
            {
                var odoValue = (int)lastOdometer.Value;

                if (isFirmwareL)
                {
                    // Firmware "L": odometer_km is reliable, use directly
                    if (odoValue != vehicle.Mileage)
                    {
                        vehicle.Mileage = odoValue;
                        vehicle.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        source = "gps";
                        updated = true;
                    }
                }
                else
                {
                    // Non firmware "L": only update if odometer > current mileage
                    if (odoValue > vehicle.Mileage)
                    {
                        vehicle.Mileage = odoValue;
                        vehicle.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync(ct);
                        source = "gps";
                        updated = true;
                    }
                }
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
