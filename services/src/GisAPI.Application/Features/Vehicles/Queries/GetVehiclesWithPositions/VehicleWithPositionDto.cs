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
    // Tension batterie en volts, calculée côté serveur (voir BatteryReadout) :
    // NEMS = minimum du jour de l'octet « Batterie » (34-36) × 40/256 ;
    // Teltonika = power_voltage × 0,1 de la dernière trame. Exposée à côté de
    // BatteryLevel : l'exploitant préfère les volts au monitoring.
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
    DateTime? EngineOffSince,
    // NEMS : BatteryVoltage/BatteryLevel sont le MINIMUM du jour (minuit →
    // minuit, heure de Tunis), et non la dernière trame. Le temps réel ne doit
    // alors que les faire baisser, jamais les remplacer.
    bool BatteryIsDailyMin = false,
    // Fin (exclue) de la journée de ce minimum, en UTC. Passé cet instant,
    // l'écran n'affiche plus le minimum d'hier.
    DateTime? BatteryDayEndUtc = null
);