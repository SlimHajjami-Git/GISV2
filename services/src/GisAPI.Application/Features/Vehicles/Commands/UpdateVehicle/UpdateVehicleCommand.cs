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
    int? AssignedSupervisorId
) : ICommand;



