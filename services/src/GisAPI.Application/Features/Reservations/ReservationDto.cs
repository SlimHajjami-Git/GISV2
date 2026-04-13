namespace GisAPI.Application.Features.Reservations;

public record ReservationDto(
    int Id,
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    int? RequestedByUserId,
    string? RequestedByUserName,
    int? AssignedDriverId,
    string? AssignedDriverName,
    string? Purpose,
    string? Destination,
    DateTime StartDateTime,
    DateTime EndDateTime,
    int? EstimatedKm,
    int? ActualKm,
    int? StartMileage,
    int? EndMileage,
    string Status,
    string? Notes,
    DateTime CreatedAt
);

public record AvailableVehicleDto(
    int Id,
    string Name,
    string? Plate,
    int Mileage,
    bool HasGps,
    bool IsRented,
    bool HasActiveReservation
);
