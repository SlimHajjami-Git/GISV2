using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// "Argus tunisien" — curated market knowledge for ONE car model/generation
/// sold on the Tunisian market. GLOBAL reference data (no CompanyId, no tenant
/// filter): it feeds the public AI automobile assistant's buying-advisor tools
/// (recommendations, market prices, known defects, parts costs, resale).
///
/// One row = one (brand, model, generation) with its curated facts. The three
/// *Json columns hold jsonb arrays kept schema-light on purpose — the data is
/// curated and read-only at runtime, updated via the seed file / back-office:
///   KnownIssuesJson : [{ "issue", "severity", "kmWindow", "checkTip" }]
///   PartsPricesJson : [{ "part", "priceTnd", "note" }]
///   PriceCurveJson  : [{ "year", "km", "priceTnd" }]  // médiane occasion TN
/// All prices are INDICATIVE TND figures; UpdatedAt tracks data freshness.
/// </summary>
public class CarMarketModel : AuditableEntity
{
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>Human generation label, e.g. "Clio 4" or "2017-2021".</summary>
    public string? Generation { get; set; }

    public int YearFrom { get; set; }
    /// <summary>Null = still sold new in Tunisia (NewPriceTnd applies).</summary>
    public int? YearTo { get; set; }

    /// <summary>populaire | citadine | compacte | berline | suv | pickup | utilitaire</summary>
    public string Segment { get; set; } = string.Empty;

    /// <summary>essence | diesel</summary>
    public string Energy { get; set; } = string.Empty;

    public string? EngineLabel { get; set; }

    /// <summary>Chevaux fiscaux — critère d'achat majeur en Tunisie (vignette, assurance, "voiture populaire" = 4 CV).</summary>
    public int? FiscalPower { get; set; }

    public int? Seats { get; set; }

    /// <summary>Prix neuf actuel en TND si encore commercialisé, sinon null.</summary>
    public decimal? NewPriceTnd { get; set; }

    /// <summary>Consommation réelle constatée (L/100km), pas la valeur constructeur.</summary>
    public double? RealConsumptionL100 { get; set; }

    /// <summary>Fiabilité générale sur 10 (curatée).</summary>
    public int? ReliabilityScore { get; set; }

    /// <summary>Conseil d'achat libre (points forts/faibles, versions à préférer/éviter).</summary>
    public string? BuyingAdvice { get; set; }

    public string KnownIssuesJson { get; set; } = "[]";
    public string PartsPricesJson { get; set; } = "[]";
    public string PriceCurveJson { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
}
