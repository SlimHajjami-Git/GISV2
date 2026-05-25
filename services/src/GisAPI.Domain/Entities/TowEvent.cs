using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// A confirmed/suspected vehicle TOW, detected independently of any accident.
///
/// <para>Detection rule (operator-defined): a vehicle whose engine is OFF
/// (<c>ignition_on = false</c>) yet reports a speed above 15 km/h AND a real
/// change of position cannot be driving itself — it is being towed. To avoid
/// false positives from a single jittery GPS fix, detection requires a
/// <i>cluster</i> of consecutive qualifying frames (the "nuage de points")
/// plus a meaningful cumulative displacement.</para>
///
/// <para>One row represents one tow "trip": it is created when the cluster is
/// first confirmed and then extended (LastSeenAt / distance / max speed) while
/// the motion continues, and marked <c>ended</c> once the vehicle stops
/// reporting tow-like frames for a while.</para>
/// </summary>
public class TowEvent : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int DeviceId { get; set; }
    public GpsDevice? Device { get; set; }

    public string? DeviceUid { get; set; }

    /// <summary>First qualifying frame of the tow trip.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>Most recent qualifying frame seen so far (extended live).</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Set once the trip is considered finished (no motion for a while).</summary>
    public DateTime? EndedAt { get; set; }

    public double StartLat { get; set; }
    public double StartLon { get; set; }
    public double? LastLat { get; set; }
    public double? LastLon { get; set; }

    /// <summary>Best-effort reverse-geocoded address of the start point.</summary>
    public string? StartAddress { get; set; }

    public double MaxSpeedKph { get; set; }
    public double DistanceMeters { get; set; }
    public int FrameCount { get; set; }

    /// <summary><c>active</c> while motion continues, <c>ended</c> afterwards.</summary>
    public string Status { get; set; } = "active";

    public bool Acknowledged { get; set; }
    public int? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
