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
    int? GpsDeviceId,
    DateTime? LastCommunication,
    bool IsOnline,
    PositionDto? LastPosition,
    VehicleStatsDto? Stats,
    int Mileage,
    // Battery health flag — true when VoltageHealthMonitoringService
    // raised an alert in the last 7 days. Drives the warning indicator
    // on the monitoring page so an admin sees at a glance which
    // vehicles need a battery check. Stays sticky for a week so the
    // alert doesn't disappear after the 48h cooldown silences the
    // detector.
    bool HasBatteryHealthAlert,
    // Operator-toggled immobilisation. While true, every automatic
    // alert service skips this vehicle (mechanic intervention, long-
    // term parking, boîtier removed for maintenance, …). Surfaced on
    // monitoring so the operator can see at a glance which vehicles
    // are deliberately muted from the alert pipeline.
    bool IsImmobilized,
    string? ImmobilizationReason,
    DateTime? ImmobilizationStartedAt
);

public record PositionDto(
    int Id,
    double Latitude,
    double Longitude,
    double SpeedKph,
    double CourseDeg,
    bool IgnitionOn,
    DateTime RecordedAt,
    int? FuelRaw,
    short? TemperatureC,
    int? BatteryLevel,
    string? Address,
    long? OdometerKm,
    // Decoded battery voltage in volts (PowerVoltage byte * 0.3, the
    // empirical NEMS L 12V-system calibration). Computed server-side
    // and exposed alongside BatteryLevel so the frontend can show
    // either readout — the operator likes V on monitoring but % on
    // the device-overview screen.
    double? BatteryVoltage
);

/// <summary>
/// Vehicle statistics for monitoring display
/// </summary>
public record VehicleStatsDto(
    double CurrentSpeed,           // Vitesse actuelle (km/h)
    double MaxSpeed,               // Plus grosse vitesse atteinte (km/h)
    int? FuelLevel,                // Niveau de carburant (%)
    short? Temperature,            // Temperature moteur (C)
    int? BatteryLevel,             // Niveau batterie (%)
    double? BatteryVoltage,        // Tension batterie (V) — préféré côté UI
    bool IsMoving,                 // En circulation
    bool IsStopped,                // En arret
    TimeSpan MovingTime,           // Temps en circulation
    TimeSpan StoppedTime,          // Temps en arret
    DateTime? LastStopTime,        // Dernier arret
    DateTime? LastMoveTime,        // Dernier mouvement
    // Timestamp of the most recent frame where ignition_on was true.
    // Everything after that point the engine has been off — drives the
    // "moteur coupé depuis X min" copy on the monitoring detail panel.
    // Null when the vehicle has no recorded ignition-on frame.
    DateTime? EngineOffSince
);