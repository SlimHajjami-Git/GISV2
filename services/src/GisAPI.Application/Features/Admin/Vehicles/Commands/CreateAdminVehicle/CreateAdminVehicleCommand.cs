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
    string? GpsFuelSensorMode,
    string? GpsSimNumber,
    string? GpsSimOperator,
    DateTime? GpsInstallationDate
) : IRequest<CreateAdminVehicleResult>;

/// <param name="ReleaseSuggestedDeviceId">
/// Fiche boîtier STRICTEMENT VIDE (jamais connectée, sans véhicule ni donnée) qui porte le MAT ou
/// la SIM refusés : l'écran propose de la libérer puis de recommencer (recette du 09/10/2026).
/// </param>
public record CreateAdminVehicleResult(
    bool Success, string? Error = null, AdminVehicleDto? Vehicle = null, int? ReleaseSuggestedDeviceId = null);
