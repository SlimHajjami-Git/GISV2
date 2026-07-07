using System;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesStatus;

/// <summary>
/// Lightweight vehicle online/offline status for the header "offline bell".
/// Deliberately does NOT touch <c>gps_positions</c> — <see cref="IsOnline"/> is
/// derived purely from <c>GpsDevice.LastCommunication</c>, so the poll that runs
/// on EVERY page (OfflineVehiclesService) stays a single indexed query instead of
/// loading 24h of positions for the whole fleet like /vehicles/with-positions does.
/// </summary>
public record VehicleStatusDto(
    int Id,
    string Name,
    string? Plate,
    bool IsOnline,
    DateTime? LastCommunication);
