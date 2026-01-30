using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicles;

public record GetVehiclesQuery(
    string? SearchTerm = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<VehicleDto>>;

public record VehicleDto(
    int Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string Status,
    bool HasGps,
    int Mileage,
    int? AssignedDriverId,
    string? AssignedDriverName,
    int? AssignedSupervisorId,
    string? AssignedSupervisorName,
    GpsDeviceDto? GpsDevice,
    DateTime CreatedAt
);

public record GpsDeviceDto(
    int Id,
    string DeviceUid,
    string? Label,
    string Status,
    DateTime? LastCommunication,
    int? BatteryLevel,
    int? SignalStrength,
    string? Model,
    string? FirmwareVersion
);



