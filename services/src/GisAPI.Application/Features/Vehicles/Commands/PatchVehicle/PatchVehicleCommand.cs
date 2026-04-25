using MediatR;

namespace GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;

public record PatchVehicleCommand(
    int Id,
    int? SpeedLimit,
    int? DepartmentId,
    string? FuelType,
    // Identification
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    int? Mileage,
    int? FuelTankCapacity,
    // Acquisition
    string? AcquisitionType,
    decimal? PurchasePrice,
    decimal? LeasingMonthlyPayment,
    int? LeasingDurationMonths,
    DateTime? LeasingStartDate,
    int? LeasingPaymentDay,
    DateTime? RegistrationDate,
    DateTime? PurchaseDate
) : IRequest<Unit>;



