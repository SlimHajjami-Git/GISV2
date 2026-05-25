using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits;

/// <summary>
/// Builds the NEMS over-speed configuration command for a GPS boitier.
///
/// Operator-confirmed format (Calypso 9 p6):
///     AJ+CONFN=101,3,2,&lt;speed&gt;,0,0,&lt;password&gt;
/// where &lt;speed&gt; is the only variable field, expressed in TENTHS OF MPH
/// (e.g. 377 = 37.7 MPH); 101,3,2 and the trailing 0,0 are constant.
///
/// Centralised here so the single-vehicle setter and the bulk
/// device-sync use byte-identical command text (no drift).
/// </summary>
public static class SpeedLimitCommandBuilder
{
    private const double KmhToMph = 0.621371;

    /// <summary>km/h → device value (tenths of MPH), rounded half-up.</summary>
    public static int ToDeviceTenthsMph(int speedLimitKmh) =>
        (int)Math.Round(speedLimitKmh * KmhToMph * 10.0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// True only for NEMS-family boitiers, which speak the AJ+ protocol
    /// (AJ+GO / AJ+STOP / AJ+CONFN). Operator constraint (Calypso 9 p6):
    /// the AJ+CONFN speed command must NEVER be sent to a Noron device —
    /// Noron uses a different protocol and would mis-handle it.
    ///
    /// Detection (matches the fleet's data shapes):
    ///   - protocol_type 'gps_type_1' → AJ+ family (NEMS), OR
    ///   - brand/model contains "NEMS" (covers the units whose
    ///     protocol_type column is blank but are branded NEMS).
    /// Noron (protocol_type 'noron', brand NORON) fails both → excluded.
    /// </summary>
    public static bool IsNemsDevice(GpsDevice? device)
    {
        if (device == null) return false;
        if (string.Equals(device.ProtocolType, "noron", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(device.ProtocolType, "gps_type_1", StringComparison.OrdinalIgnoreCase))
            return true;
        var brand = device.Brand ?? string.Empty;
        var model = device.Model ?? string.Empty;
        return brand.Contains("NEMS", StringComparison.OrdinalIgnoreCase)
            || model.Contains("NEMS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Full command text (newline included) ready to hand to
    /// IRustCommandPusher / store in DeviceCommand.CommandText.
    /// </summary>
    public static string Build(int speedLimitKmh, string? deviceCommandGo)
    {
        var tenths = ToDeviceTenthsMph(speedLimitKmh);
        var password = ExtractPassword(deviceCommandGo);
        return $"AJ+CONFN=101,3,2,{tenths},0,0,{password}\n";
    }

    /// <summary>
    /// Pull the protection code (e.g. "#9999") out of a device's stored
    /// CommandGo ("AJ+GO#9999\n"). Falls back to the platform default when
    /// the device has no custom code.
    /// </summary>
    public static string ExtractPassword(string? commandGo)
    {
        if (string.IsNullOrWhiteSpace(commandGo)) return "#9999";
        var hash = commandGo.IndexOf('#');
        if (hash < 0) return "#9999";
        return commandGo.Substring(hash).Trim();
    }
}
