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

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == request.VehicleId && v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .Include(v => v.Documents)
            .Include(v => v.MaintenanceRecords.OrderByDescending(m => m.Date).Take(10))
                .ThenInclude(m => m.Supplier)
            .FirstOrDefaultAsync(ct);

        if (vehicle == null)
            return null;

        var now = DateTime.UtcNow;

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
            vehicle.Mileage,
            vehicle.RentalMileage,
            
            // Driver info
            vehicle.AssignedDriverId,
            vehicle.AssignedDriver?.Name,
            vehicle.AssignedDriver?.Email,
            vehicle.DriverName,
            vehicle.DriverPhone,
            
            // Supervisor info
            vehicle.AssignedSupervisorId,
            vehicle.AssignedSupervisor?.Name,
            
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
            
            // Document expiries
            vehicle.InsuranceExpiry,
            vehicle.TechnicalInspectionExpiry,
            vehicle.TaxExpiry,
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
                m.Supplier?.Name,
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
