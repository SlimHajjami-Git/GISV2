namespace GisAPI.DTOs;

public record CreateVehicleRequest(
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    int Mileage = 0
);

public record UpdateVehicleRequest(
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string Status,
    int Mileage,
    int? AssignedDriverId,
    int? AssignedSupervisorId,
    string? FuelType,
    int? FuelTankCapacity,
    // Acquisition
    string? AcquisitionType,
    decimal? PurchasePrice,
    decimal? LeasingMonthlyPayment,
    DateTime? RegistrationDate,
    // Document dates
    DateTime? InsuranceStartDate,
    DateTime? InsuranceExpiry,
    int? InsuranceReminderDays,
    DateTime? TaxStartDate,
    DateTime? TaxExpiry,
    int? TaxReminderDays,
    DateTime? TechnicalInspectionStartDate,
    DateTime? TechnicalInspectionExpiry,
    int? TechnicalInspectionReminderDays
);

public record VehicleDto(
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
    int? AssignedDriverId,
    string? AssignedDriverName,
    int? AssignedSupervisorId,
    string? AssignedSupervisorName,
    GpsDeviceDto? GpsDevice,
    DateTime CreatedAt
);

public record GpsDeviceDto(
    int Id,
    string DeviceUid,
    string? Label,
    string Status,
    DateTime? LastCommunication,
    int? BatteryLevel,
    int? SignalStrength,
    string? Model,
    string? FirmwareVersion
);

public record VehicleLocationDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    double Latitude,
    double Longitude,
    double? Speed,
    double? Course,
    bool? IgnitionOn,
    DateTime RecordedAt,
    bool IsOnline
);
