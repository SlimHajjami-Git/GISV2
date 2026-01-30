using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

/// <summary>
/// Command to process and broadcast a GPS position update via SignalR
/// Implements adaptive interval logic based on vehicle state
/// </summary>
public record BroadcastPositionCommand(
    string DeviceUid,
    double Latitude,
    double Longitude,
    double? SpeedKph,
    double? CourseDeg,
    bool? IgnitionOn,
    DateTime RecordedAt,
    string? AlertType = null
) : ICommand<BroadcastPositionResult>;

public record BroadcastPositionResult(
    bool Broadcasted,
    int? VehicleId,
    string? SkipReason
);



