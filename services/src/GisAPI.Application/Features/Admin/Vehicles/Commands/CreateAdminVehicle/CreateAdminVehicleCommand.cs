using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using MediatR;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;

public record CreateAdminVehicleCommand(
    string Name,
    string? Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string? Status,
    bool HasGps,
    int? Mileage,
    string? FuelType,
    int? FuelTankCapacity,
    int CompanyId,
    int? GpsDeviceId,
    string? GpsImei,
    string? GpsMat,
    string? GpsBrand,
    string? GpsModel,
    string? GpsFirmwareVersion,
    string? GpsFuelSensorMode
) : IRequest<CreateAdminVehicleResult>;

public record CreateAdminVehicleResult(bool Success, string? Error = null, AdminVehicleDto? Vehicle = null);
