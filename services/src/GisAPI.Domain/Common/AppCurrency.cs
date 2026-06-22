namespace GisAPI.Domain.Common;

/// <summary>
/// Deployment-wide default currency (ISO code) used for DISPLAY and as the seed
/// default for new companies. Set ONCE at startup from configuration
/// <c>App:DefaultCurrency</c> (env <c>App__DefaultCurrency</c>):
/// <list type="bullet">
///   <item>Tunisie / Calypso build → "TND" (the built-in default)</item>
///   <item>Algérie / Bougeo build  → "DZD" (set via the K8s configmap)</item>
/// </list>
/// Mirrors the frontend's per-deployment <c>environment.defaultCurrency</c> so the
/// API (AI assistant, chart units, report units, new-company defaults) speaks the
/// same currency as the UI. It is a mutable static rather than injected config so
/// plain entities (e.g. <see cref="Entities.SocieteSettings"/>) and the many
/// MediatR handlers can read it without taking an IConfiguration dependency.
/// </summary>
public static class AppCurrency
{
    /// <summary>ISO currency code, e.g. "TND" or "DZD". Defaults to "TND" until set at startup.</summary>
    public static string Default { get; set; } = "TND";
}
