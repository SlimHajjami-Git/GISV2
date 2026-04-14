using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;

public record UpdateVehicleCommand(
    int Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string Status,
    int Mileage,
    string? FuelType,
    int? FuelTankCapacity,
    int? AssignedDriverId,
    int? AssignedSupervisorId,
    // Acquisition info
    string? AcquisitionType,
    decimal? PurchasePrice,
    decimal? LeasingMonthlyPayment,
    int? LeasingDurationMonths,
    DateTime? LeasingStartDate,
    int? LeasingPaymentDay,
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
) : ICommand;
