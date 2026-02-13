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
    PositionDto? LastPosition,
    VehicleStatsDto? Stats,
    int Mileage
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
    long? OdometerKm
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
    bool IsMoving,                 // En circulation
    bool IsStopped,                // En arret
    TimeSpan MovingTime,           // Temps en circulation
    TimeSpan StoppedTime,          // Temps en arret
    DateTime? LastStopTime,        // Dernier arret
    DateTime? LastMoveTime         // Dernier mouvement
);