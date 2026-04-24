namespace GisAPI.Application.Features.Drivers;

public record DriverDto(
    int Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignedVehicleId,
    string? AssignedVehicleName,
    string? AssignedVehiclePlate,
    string Status,
    DateTime CreatedAt
);
