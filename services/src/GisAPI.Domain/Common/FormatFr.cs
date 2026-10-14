using System.Globalization;

namespace GisAPI.Domain.Common;

/// <summary>
/// Nombres au format français pour les TEXTES lus par l'utilisateur (notifications, refus de
/// saisie, e-mails, PDF) : « 45 000 km », « 12,5 L/100km ». Sans fournisseur, {x:N0} et {x:F1}
/// suivent la culture du serveur, invariante dans le conteneur Linux, et écrivaient « 45,000 km »
/// (lu 45 km) et « 12.5 » (recette de Karim du 14/10/2026).
///
/// Usage : <c>$"… {FormatFr.N(km, 0)} km"</c> au lieu de <c>{km:N0}</c>. Espace insécable ordinaire
/// (U+00A0) et non l'espace fine de fr-FR : les polices des PDF et des e-mails ne l'ont pas
/// toujours. Jamais pour une clé de cache, du SQL ou un journal technique.
/// </summary>
public static class FormatFr
{
    public static readonly NumberFormatInfo Nombres = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberDecimalSeparator = ",",
    };

    /// <summary>Équivalent de <c>{x:N&lt;d&gt;}</c> : milliers séparés, d décimales.</summary>
    public static string N(double value, int decimals) => value.ToString("N" + decimals, Nombres);
    public static string N(decimal value, int decimals) => value.ToString("N" + decimals, Nombres);
    public static string N(long value, int decimals) => value.ToString("N" + decimals, Nombres);
    public static string N(double? value, int decimals) => value.HasValue ? N(value.Value, decimals) : "";
    public static string N(decimal? value, int decimals) => value.HasValue ? N(value.Value, decimals) : "";
    public static string N(long? value, int decimals) => value.HasValue ? N(value.Value, decimals) : "";

    /// <summary>Équivalent de <c>{x:F&lt;d&gt;}</c> : d décimales, virgule, sans séparateur de milliers.</summary>
    public static string F(double value, int decimals) => value.ToString("F" + decimals, Nombres);
    public static string F(decimal value, int decimals) => value.ToString("F" + decimals, Nombres);
    public static string F(double? value, int decimals) => value.HasValue ? F(value.Value, decimals) : "";
}
