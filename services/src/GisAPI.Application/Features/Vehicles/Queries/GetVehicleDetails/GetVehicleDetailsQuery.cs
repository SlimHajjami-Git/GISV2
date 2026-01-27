using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicleDetails;

public record GetVehicleDetailsQuery(int VehicleId) : IQuery<VehicleDetailsDto?>;

public record VehicleDetailsDto(
    int Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string Status,
    bool HasGps,
    int Mileage,
    int? RentalMileage,
    
    // Driver info
    int? AssignedDriverId,
    string? AssignedDriverName,
    string? AssignedDriverEmail,
    string? DriverName,
    string? DriverPhone,
    
    // Supervisor info
    int? AssignedSupervisorId,
    string? AssignedSupervisorName,
    
    // GPS Device
    VehicleGpsDeviceDto? GpsDevice,
    
    // Document expiries
    DateTime? InsuranceExpiry,
    DateTime? TechnicalInspectionExpiry,
    DateTime? TaxExpiry,
    DateTime? RegistrationExpiry,
    DateTime? TransportPermitExpiry,
    
    // Related data
    List<VehicleDocumentDto> Documents,
    List<MaintenanceRecordDto> RecentMaintenance,
    
    // Metadata
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record VehicleGpsDeviceDto(
    int Id,
    string DeviceUid,
    string? Label,
    string? Mat,
    string Status,
    DateTime? LastCommunication,
    int? BatteryLevel,
    int? SignalStrength,
    string? Model,
    string? FirmwareVersion,
    string? ProtocolType
);

public record VehicleDocumentDto(
    int Id,
    string Type,
    string Name,
    DateTime? ExpiryDate,
    string? FileUrl,
    DateTime CreatedAt,
    string ExpiryStatus
);

public record MaintenanceRecordDto(
    int Id,
    string Type,
    string? Description,
    DateTime Date,
    int? MileageAtService,
    decimal? Cost,
    string? SupplierName,
    string Status
);
