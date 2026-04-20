namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Pushes a pending device command to the Rust GPS ingest for immediate
/// over-the-socket delivery, so the operator doesn't have to wait for the
/// device's next frame (up to 30s on LBS-only polls).
///
/// The caller is expected to have already inserted the pending row in the
/// <c>device_commands</c> table — the push is a latency optimisation. If the
/// push fails (device not connected, Rust unreachable, etc.), the per-frame
/// DB poll in Rust still delivers the command on the next incoming frame.
/// </summary>
public interface IRustCommandPusher
{
    /// <param name="deviceId">gps_devices.id</param>
    /// <param name="command">Full command text (e.g. "AJ+GO#1311\n" or "AJ+STOP#1311\n") — newline included.</param>
    Task<RustPushResult> PushAsync(int deviceId, string command, CancellationToken ct = default);
}

public enum RustPushOutcome
{
    /// <summary>200 OK — command delivered to the live TCP socket.</summary>
    Pushed,
    /// <summary>404 — device not currently connected; fallback DB poll will deliver.</summary>
    DeviceNotConnected,
    /// <summary>500 or network error — Rust could not send; fallback DB poll will deliver.</summary>
    Error,
}

public record RustPushResult(RustPushOutcome Outcome, string? Message = null);
