using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Represents a command sent to a GPS device (e.g., AJ+STOP, AJ+GO).
/// Commands are stored in the database for audit and security — nothing is hardcoded.
/// The Rust GPS ingest service reads pending commands and sends them over TCP.
/// </summary>
public class DeviceCommand : TenantEntity
{
    public int DeviceId { get; set; }
    public GpsDevice? Device { get; set; }

    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>
    /// The user who initiated the command (operator who clicked stop/go)
    /// </summary>
    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// Command type: 'STOP' or 'GO'
    /// </summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// The raw command text to send to the device (e.g., "AJ+STOP#1311\n")
    /// Stored in DB so nothing is hardcoded — can vary per device/protocol
    /// </summary>
    public string CommandText { get; set; } = string.Empty;

    /// <summary>
    /// Command status: 'pending', 'sent', 'acknowledged', 'failed', 'expired'
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>
    /// When the command was actually sent to the device via TCP
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Number of send attempts (device may be offline)
    /// </summary>
    public int Attempts { get; set; } = 0;

    /// <summary>
    /// Error message if the command failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 'manual' = operator action, 'auto_recovery' = automatic AJ+GO after unwanted stop
    /// </summary>
    public string Source { get; set; } = "manual";
}
