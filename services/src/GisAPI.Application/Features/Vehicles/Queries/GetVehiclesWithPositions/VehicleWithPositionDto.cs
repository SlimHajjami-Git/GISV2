using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

public record VehicleWithPositionDto(
    int Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    string Status,
    bool HasGps,
    string? DeviceUid,
    DateTime? LastCommunication,
    bool IsOnline,
    PositionDto? LastPosition
);

public record PositionDto(
    int Id,
    double Latitude,
    double Longitude,
    double SpeedKph,
    double CourseDeg,
    bool IgnitionOn,
    DateTime RecordedAt
);