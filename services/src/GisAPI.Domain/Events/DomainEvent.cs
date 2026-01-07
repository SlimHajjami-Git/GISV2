namespace GisAPI.Domain.Events;

public abstract record DomainEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

// GPS Events
public record GpsPositionReceivedEvent(
    int DeviceId,
    string DeviceUid,
    double Latitude,
    double Longitude,
    double? Speed,
    DateTime Timestamp
) : DomainEvent;

public record VehicleEnteredGeofenceEvent(
    int VehicleId,
    int GeofenceId,
    string GeofenceName,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : DomainEvent;

public record VehicleExitedGeofenceEvent(
    int VehicleId,
    int GeofenceId,
    string GeofenceName,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : DomainEvent;

public record SpeedingAlertEvent(
    int VehicleId,
    int? GeofenceId,
    double Speed,
    double SpeedLimit,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : DomainEvent;

public record AlertCreatedEvent(
    int AlertId,
    int? VehicleId,
    string AlertType,
    string Severity,
    string Message,
    DateTime Timestamp
) : DomainEvent;

// Vehicle Events
public record VehicleCreatedEvent(int VehicleId, int CompanyId, string Name) : DomainEvent;
public record VehicleUpdatedEvent(int VehicleId, int CompanyId) : DomainEvent;
public record VehicleDeletedEvent(int VehicleId, int CompanyId) : DomainEvent;

// Maintenance Events
public record MaintenanceDueEvent(
    int VehicleId,
    int MaintenanceRecordId,
    string MaintenanceType,
    DateTime DueDate
) : DomainEvent;

public record MaintenanceCompletedEvent(
    int VehicleId,
    int MaintenanceRecordId,
    string MaintenanceType,
    decimal TotalCost
) : DomainEvent;
