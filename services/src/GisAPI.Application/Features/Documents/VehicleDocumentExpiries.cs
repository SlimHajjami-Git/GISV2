using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.Documents;

/// <summary>
/// Les échéances de document portées par un véhicule (colonnes
/// <c>vehicles."InsuranceExpiry"</c>, etc.) et leur libellé français.
/// Liste unique, partagée par l'alerte d'échéances
/// (<see cref="Queries.GetExpiryAlertsQueryHandler"/>) et le tableau de bord
/// GPA : un type ajouté ici apparaît dans les deux.
/// Libellés identiques à ceux du renouvellement (RenewDocumentCommandHandler).
/// </summary>
public static class VehicleDocumentExpiries
{
    public const string Insurance = "insurance";
    public const string TechnicalInspection = "technical_inspection";
    public const string Tax = "tax";
    public const string Registration = "registration";
    public const string TransportPermit = "transport_permit";

    /// <summary>Type et date d'échéance, dans l'ordre historique de l'écran Échéances.</summary>
    public static IEnumerable<(string Type, DateTime? Expiry)> Of(Vehicle vehicle)
    {
        yield return (Insurance, vehicle.InsuranceExpiry);
        yield return (TechnicalInspection, vehicle.TechnicalInspectionExpiry);
        yield return (Tax, vehicle.TaxExpiry);
        yield return (Registration, vehicle.RegistrationExpiry);
        yield return (TransportPermit, vehicle.TransportPermitExpiry);
    }

    /// <summary>Libellé français (tous féminins : « Assurance expirée », « Vignette à renouveler »…).</summary>
    public static string Label(string type) => type switch
    {
        Insurance => "Assurance",
        TechnicalInspection => "Visite technique",
        Tax => "Vignette",
        Registration => "Carte grise",
        TransportPermit => "Autorisation transport",
        _ => type
    };
}
