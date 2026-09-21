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
    DateTime CreatedAt,
    // Compte chauffeur relié (migration 050) : null = pas d'accès à l'application ;
    // AccountStatus = statut de ce compte (« active », « inactive »).
    int? UserId = null,
    string? AccountStatus = null
);
