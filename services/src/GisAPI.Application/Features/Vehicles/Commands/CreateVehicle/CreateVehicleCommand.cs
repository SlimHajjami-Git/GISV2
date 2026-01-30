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
    int Mileage = 0,
    int? GpsDeviceId = null,
    GpsDeviceInfo? NewGpsDevice = null
) : ICommand<int>;

public record GpsDeviceInfo(
    string DeviceUid,
    string? SimNumber,
    string? SimOperator,
    string? Brand,
    string? Model,
    DateTime? InstallationDate
);



