using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicleDetails;

public class GetVehicleDetailsQueryHandler : IRequestHandler<GetVehicleDetailsQuery, VehicleDetailsDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleDetailsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VehicleDetailsDto?> Handle(GetVehicleDetailsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .Include(v => v.Documents)
            .Include(v => v.MaintenanceRecords.OrderByDescending(m => m.Date).Take(10))
            .FirstOrDefaultAsync(ct);

        if (vehicle == null)
            return null;

        // Vehicle visibility is driven by EXPLICIT assignments: a user WITH assignments can only
        // access assigned vehicles (even if (auto-)promoted to admin); a real admin/CEO has no
        // assignments and can access any vehicle of the company.
        if (userId > 0)
        {
            var hasAnyAssignment = await _context.UserVehicles.AnyAsync(uv => uv.UserId == userId, ct);
            if (hasAnyAssignment)
            {
                var hasAccess = await _context.UserVehicles
                    .AnyAsync(uv => uv.UserId == userId && uv.VehicleId == request.VehicleId, ct);
                if (!hasAccess)
                    return null;
            }
            else if (!isAdmin)
            {
                return null;
            }
        }

        var now = DateTime.UtcNow;

        // Firmware "L": override mileage from GPS odometer_km
        var mileage = vehicle.Mileage;
        if (vehicle.GpsDevice != null
            && !string.IsNullOrEmpty(vehicle.GpsDevice.FirmwareVersion)
            && vehicle.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase))
        {
            var latestOdo = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId!.Value
                         && p.OdometerKm.HasValue && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);
            if (latestOdo.HasValue && latestOdo.Value > 0)
                mileage = (int)latestOdo.Value;
        }

        return new VehicleDetailsDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Type,
            vehicle.Brand,
            vehicle.Model,
            vehicle.Plate,
            vehicle.Year,
            vehicle.Color,
            vehicle.Status,
            vehicle.HasGps,
            mileage,
            vehicle.RentalMileage,
            
            // Driver info
            vehicle.AssignedDriverId,
            vehicle.AssignedDriver?.FullName,
            vehicle.AssignedDriver?.Email,
            vehicle.DriverName,
            vehicle.DriverPhone,
            
            // Supervisor info
            vehicle.AssignedSupervisorId,
            vehicle.AssignedSupervisor?.FullName,
            
            // GPS Device
            vehicle.GpsDevice != null ? new VehicleGpsDeviceDto(
                vehicle.GpsDevice.Id,
                vehicle.GpsDevice.DeviceUid,
                vehicle.GpsDevice.Label,
                vehicle.GpsDevice.Mat,
                vehicle.GpsDevice.Status,
                vehicle.GpsDevice.LastCommunication,
                vehicle.GpsDevice.BatteryLevel,
                vehicle.GpsDevice.SignalStrength,
                vehicle.GpsDevice.Model,
                vehicle.GpsDevice.FirmwareVersion,
                vehicle.GpsDevice.ProtocolType
            ) : null,
            
            // Acquisition info
            vehicle.AcquisitionType,
            vehicle.PurchasePrice,
            vehicle.PurchaseDate,
            vehicle.LeasingMonthlyPayment,
            vehicle.LeasingDurationMonths,
            vehicle.LeasingStartDate,
            vehicle.LeasingPaymentDay,
            vehicle.RegistrationDate,
            vehicle.FuelType,
            vehicle.FuelTankCapacity,
            vehicle.SpeedLimit,
            vehicle.DepartmentId,

            // Document dates & expiries
            vehicle.InsuranceStartDate,
            vehicle.InsuranceExpiry,
            vehicle.InsuranceReminderDays,
            vehicle.TaxStartDate,
            vehicle.TaxExpiry,
            vehicle.TaxReminderDays,
            vehicle.TechnicalInspectionStartDate,
            vehicle.TechnicalInspectionExpiry,
            vehicle.TechnicalInspectionReminderDays,
            vehicle.RegistrationExpiry,
            vehicle.TransportPermitExpiry,
            
            // Documents
            vehicle.Documents.Select(d => new VehicleDocumentDto(
                d.Id,
                d.Type,
                d.Name,
                d.ExpiryDate,
                d.FileUrl,
                d.CreatedAt,
                GetExpiryStatus(d.ExpiryDate, now)
            )).ToList(),
            
            // Recent maintenance
            vehicle.MaintenanceRecords.Select(m => new MaintenanceRecordDto(
                m.Id,
                m.Type,
                m.Description,
                m.Date,
                m.MileageAtService,
                m.TotalCost,
                m.ServiceProvider,
                m.Status
            )).ToList(),
            
            // Metadata
            vehicle.CreatedAt,
            vehicle.UpdatedAt
        );
    }

    private static string GetExpiryStatus(DateTime? expiryDate, DateTime now)
    {
        if (!expiryDate.HasValue)
            return "unknown";
            
        var daysUntilExpiry = (expiryDate.Value - now).TotalDays;
        
        if (daysUntilExpiry < 0)
            return "expired";
        if (daysUntilExpiry <= 30)
            return "expiring_soon";
        return "valid";
    }
}




