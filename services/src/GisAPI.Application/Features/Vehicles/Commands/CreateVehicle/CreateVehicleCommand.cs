using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public record CreateVehicleCommand(
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    int Mileage = 0
) : ICommand<int>;
