using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleAssignments.Queries.GetUserVehicles;

public record GetUserVehiclesQuery(int? UserId = null) : IQuery<List<UserVehicleDto>>;

public record UserVehicleDto(
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    string VehicleType,
    string? VehicleBrand,
    string? VehicleModel,
    int? GpsDeviceId,
    bool HasGps,
    DateTime AssignedAt
);
