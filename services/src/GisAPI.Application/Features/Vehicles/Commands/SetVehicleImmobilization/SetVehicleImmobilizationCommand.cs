using MediatR;

namespace GisAPI.Application.Features.Vehicles.Commands.SetVehicleImmobilization;

/// <summary>
/// Activate or deactivate the "immobilisation" flag on a vehicle. When
/// active, every automatic alert service (accident detection, voltage
/// health, speed limit, geofence, …) suppresses notifications for this
/// vehicle until the flag is cleared.
///
/// <para>Typical use cases: vehicle sent to the mechanic, long-term
/// parking, GPS boîtier removed for swap or maintenance.</para>
/// </summary>
public record SetVehicleImmobilizationCommand(
    int VehicleId,
    bool Activate,
    string? Reason
) : IRequest<SetVehicleImmobilizationResult>;

public record SetVehicleImmobilizationResult(
    int VehicleId,
    bool IsImmobilized,
    string? Reason,
    DateTime? StartedAt,
    int? StartedByUserId);
