using System.Text.Json;
using System.Text.Json.Serialization;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Services;

/// <summary>
/// LLM tools of the public automobile assistant's BUYING ADVISOR, backed by
/// the curated "Argus tunisien" table (<see cref="CarMarketModel"/>).
///
/// Design rules:
///  • Every figure the AI quotes must come from here — the system prompt
///    forbids invented prices, so tools return explicit "found: false"
///    payloads when the base doesn't cover a model.
///  • Results are compact JSON (the LLM re-reads them every round) and carry a
///    disclaimer marking prices as indicative TND values.
///  • The table is small (curated top models) — filtering in memory after a
///    cheap SQL prefilter is deliberate and keeps logic testable on SQLite.
/// </summary>
public interface ICarAdvisorService
{
    IReadOnlyList<LlmToolDefinition> Tools { get; }
    Task<string> ExecuteToolAsync(string name, string argsJson, CancellationToken ct);
}

public class CarAdvisorService : ICarAdvisorService
{
    private const string Disclaimer = "Prix indicatifs en TND (marché tunisien, base Calypso). Toujours annoncer qu'il s'agit d'estimations.";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IGisDbContext _context;

    public CarAdvisorService(IGisDbContext context) => _context = context;

    // ── Tool catalogue (OpenAI function-calling schemas) ─────────────────────

    public IReadOnlyList<LlmToolDefinition> Tools { get; } = new List<LlmToolDefinition>
    {
        new("search_car_models",
            "Recherche des modèles de voitures dans la base du marché tunisien (par nom, segment, énergie ou budget max). Utiliser pour savoir quels modèles sont couverts.",
            """
            {"type":"object","properties":{
              "query":{"type":"string","description":"Texte libre marque et/ou modèle, ex: 'polo' ou 'kia picanto'"},
              "segment":{"type":"string","enum":["populaire","citadine","compacte","berline","suv","pickup","utilitaire"]},
              "energy":{"type":"string","enum":["essence","diesel"]},
              "max_budget_tnd":{"type":"number","description":"Budget maximum en dinars tunisiens (neuf ou occasion)"}
            }}
            """),
        new("get_car_details",
            "Fiche complète d'un modèle : prix neuf TN, défauts connus à contrôler, prix des pièces d'usure en TND, cote occasion par année, conseil d'achat, fiabilité. TOUJOURS utiliser avant de conseiller un modèle précis.",
            """
            {"type":"object","required":["brand","model"],"properties":{
              "brand":{"type":"string"},"model":{"type":"string"}
            }}
            """),
        new("get_market_price",
            "Cote occasion (prix du marché tunisien en TND) d'un modèle pour une année et un kilométrage donnés. Utiliser pour évaluer une annonce ou un prix de vente.",
            """
            {"type":"object","required":["brand","model","year"],"properties":{
              "brand":{"type":"string"},"model":{"type":"string"},
              "year":{"type":"integer","description":"Année du véhicule"},
              "mileage_km":{"type":"integer","description":"Kilométrage réel si connu"}
            }}
            """),
        new("recommend_cars",
            "Recommande des voitures adaptées à un budget (TND) et aux besoins (usage, places, énergie, neuf/occasion). Utiliser dès qu'un budget d'achat est mentionné.",
            """
            {"type":"object","required":["budget_tnd"],"properties":{
              "budget_tnd":{"type":"number"},
              "usage":{"type":"string","enum":["ville","route","mixte","famille","utilitaire"]},
              "seats_min":{"type":"integer"},
              "energy":{"type":"string","enum":["essence","diesel"]},
              "condition":{"type":"string","enum":["neuf","occasion","les_deux"],"description":"Défaut: les_deux"}
            }}
            """),
        new("estimate_resale",
            "Estime la valeur de revente future d'un modèle (décote annuelle du marché tunisien). Utiliser pour comparer le coût réel de possession.",
            """
            {"type":"object","required":["brand","model","year"],"properties":{
              "brand":{"type":"string"},"model":{"type":"string"},
              "year":{"type":"integer","description":"Année du véhicule possédé/visé"},
              "horizon_years":{"type":"integer","description":"Dans combien d'années revendre (défaut 3)"}
            }}
            """),
    };

    // ── Dispatcher ───────────────────────────────────────────────────────────

    public async Task<string> ExecuteToolAsync(string name, string argsJson, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
        var a = doc.RootElement;

        object result = name switch
        {
            "search_car_models" => await SearchAsync(Str(a, "query"), Str(a, "segment"), Str(a, "energy"), Num(a, "max_budget_tnd"), ct),
            "get_car_details"   => await DetailsAsync(Str(a, "brand") ?? "", Str(a, "model") ?? "", ct),
            "get_market_price"  => await MarketPriceAsync(Str(a, "brand") ?? "", Str(a, "model") ?? "", Int(a, "year") ?? 0, Int(a, "mileage_km"), ct),
            "recommend_cars"    => await RecommendAsync(Num(a, "budget_tnd") ?? 0, Str(a, "usage"), Int(a, "seats_min"), Str(a, "energy"), Str(a, "condition"), ct),
            "estimate_resale"   => await ResaleAsync(Str(a, "brand") ?? "", Str(a, "model") ?? "", Int(a, "year") ?? 0, Int(a, "horizon_years") ?? 3, ct),
            _ => new { error = $"Outil inconnu: {name}" }
        };

        return JsonSerializer.Serialize(result);
    }

    // ── Tools ────────────────────────────────────────────────────────────────

    private async Task<object> SearchAsync(string? query, string? segment, string? energy, decimal? maxBudget, CancellationToken ct)
    {
        var models = await ActiveModelsAsync(ct);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            models = models.Where(m => terms.All(t => MatchesTerm(m, t))).ToList();
        }
        if (!string.IsNullOrWhiteSpace(segment))
            models = models.Where(m => m.Segment.Equals(segment, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(energy))
            models = models.Where(m => m.Energy.Equals(energy, StringComparison.OrdinalIgnoreCase)).ToList();
        if (maxBudget is > 0)
            models = models.Where(m => CheapestOption(m) is { } p && p <= maxBudget).ToList();

        return new
        {
            found = models.Count > 0,
            count = models.Count,
            models = models.Take(10).Select(Summary),
            disclaimer = Disclaimer
        };
    }

    private async Task<object> DetailsAsync(string brand, string model, CancellationToken ct)
    {
        var m = await FindModelAsync(brand, model, ct);
        if (m == null) return NotCovered(brand, model);

        return new
        {
            found = true,
            fiche = Summary(m),
            conseilAchat = m.BuyingAdvice,
            defautsConnus = Raw(m.KnownIssuesJson),
            prixPiecesTnd = Raw(m.PartsPricesJson),
            coteOccasionTnd = Raw(m.PriceCurveJson),
            disclaimer = Disclaimer
        };
    }

    private async Task<object> MarketPriceAsync(string brand, string model, int year, int? mileageKm, CancellationToken ct)
    {
        if (year < 1990) return new { found = false, message = "Année manquante ou invalide — redemander l'année du véhicule." };

        var m = await FindModelAsync(brand, model, ct);
        if (m == null) return NotCovered(brand, model);

        var curve = Curve(m);
        if (curve.Count == 0)
            return new { found = false, message = $"Pas de cote occasion enregistrée pour {m.Brand} {m.Model}.", disclaimer = Disclaimer };

        var reference = curve.OrderBy(p => Math.Abs(p.Year - year)).First();
        var price = (double)reference.PriceTnd;

        // Adjust for mileage vs the reference point: ±1.5% per 10 000 km,
        // capped at ±15% — a coarse but honest market heuristic.
        string? kmNote = null;
        if (mileageKm is > 0)
        {
            var deltaPct = Math.Clamp((reference.Km - mileageKm.Value) / 10_000.0 * 1.5, -15, 15);
            price *= 1 + deltaPct / 100.0;
            kmNote = $"Ajusté de {deltaPct:+0.#;-0.#}% pour {mileageKm} km (référence {reference.Km} km).";
        }

        return new
        {
            found = true,
            vehicule = $"{m.Brand} {m.Model} {year}",
            estimationTnd = Math.Round(price / 100) * 100,
            reference = new { reference.Year, reference.Km, prixMedianTnd = reference.PriceTnd },
            noteKilometrage = kmNote,
            defautsAControler = Raw(m.KnownIssuesJson),
            disclaimer = Disclaimer
        };
    }

    private async Task<object> RecommendAsync(decimal budget, string? usage, int? seatsMin, string? energy, string? condition, CancellationToken ct)
    {
        if (budget <= 0) return new { found = false, message = "Budget manquant — demander le budget en TND." };

        var segments = usage?.ToLowerInvariant() switch
        {
            "ville"       => new[] { "populaire", "citadine" },
            "route"       => new[] { "compacte", "berline", "suv" },
            "famille"     => new[] { "compacte", "berline", "suv" },
            "utilitaire"  => new[] { "pickup", "utilitaire" },
            "mixte"       => new[] { "citadine", "compacte", "berline", "suv" },
            _             => null
        };

        var models = await ActiveModelsAsync(ct);
        var wantNeuf = condition is null or "neuf" or "les_deux";
        var wantOccasion = condition is null or "occasion" or "les_deux";

        var candidates = new List<(int Fiabilite, object Payload)>();
        // Nearest grounded options ABOVE budget, so a "nothing fits" answer can
        // still propose something real ("à partir de X TND vous avez…").
        var above = new List<(decimal Price, object Payload)>();

        foreach (var m in models)
        {
            if (segments != null && !segments.Contains(m.Segment, StringComparer.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(energy) && !m.Energy.Equals(energy, StringComparison.OrdinalIgnoreCase)) continue;
            if (seatsMin is > 0 && (m.Seats ?? 5) < seatsMin) continue;

            var neufOk = wantNeuf && m.NewPriceTnd is { } np && np <= budget;
            var occasion = wantOccasion
                ? Curve(m).Where(p => p.PriceTnd <= budget).OrderByDescending(p => p.Year).FirstOrDefault()
                : null;

            if (!neufOk && occasion == null)
            {
                var cheapestAbove = new List<(decimal Price, string Type, int? Year, int? Km)>();
                if (wantOccasion)
                {
                    var pt = Curve(m).Where(p => p.PriceTnd > budget).OrderBy(p => p.PriceTnd).FirstOrDefault();
                    if (pt != null) cheapestAbove.Add((pt.PriceTnd, "occasion", pt.Year, pt.Km));
                }
                if (wantNeuf && m.NewPriceTnd is { } np2 && np2 > budget)
                    cheapestAbove.Add((np2, "neuf", null, null));

                if (cheapestAbove.Count > 0)
                {
                    var best = cheapestAbove.OrderBy(o => o.Price).First();
                    above.Add((best.Price, new
                    {
                        vehicule = $"{m.Brand} {m.Model}",
                        type = best.Type,
                        annee = best.Year,
                        km = best.Km,
                        prixTnd = best.Price,
                        fiabiliteSur10 = m.ReliabilityScore
                    }));
                }
                continue;
            }

            candidates.Add((m.ReliabilityScore ?? 0, new
            {
                summary = Summary(m),
                options = new
                {
                    neuf = neufOk ? new { prixTnd = m.NewPriceTnd } : null,
                    occasion = occasion != null ? new { occasion.Year, occasion.Km, prixMedianTnd = occasion.PriceTnd } : null
                },
                conseil = m.BuyingAdvice
            }));
        }

        var ranked = candidates
            .OrderByDescending(c => c.Fiabilite)
            .Select(c => c.Payload)
            .Take(6)
            .ToList();

        return new
        {
            found = ranked.Count > 0,
            budgetTnd = budget,
            count = ranked.Count,
            candidats = ranked,
            alternativesAuDessusDuBudget = ranked.Count == 0
                ? above.OrderBy(a => a.Price).Select(a => a.Payload).Take(3).ToList()
                : null,
            message = ranked.Count == 0
                ? "Aucun modèle de la base ne rentre dans ce budget — proposer les alternatives les plus proches au-dessus (chiffres fournis) et/ou suggérer d'élargir les critères."
                : null,
            disclaimer = Disclaimer
        };
    }

    private async Task<object> ResaleAsync(string brand, string model, int year, int horizonYears, CancellationToken ct)
    {
        if (year < 1990) return new { found = false, message = "Année manquante ou invalide — redemander l'année du véhicule." };

        var m = await FindModelAsync(brand, model, ct);
        if (m == null) return NotCovered(brand, model);

        var curve = Curve(m).OrderBy(p => p.Year).ToList();
        if (curve.Count == 0)
            return new { found = false, message = $"Pas de cote pour {m.Brand} {m.Model}.", disclaimer = Disclaimer };

        // Annual depreciation from consecutive curve points; sane fallback when
        // the curve has a single point.
        var rates = new List<double>();
        for (var i = 0; i + 1 < curve.Count; i++)
        {
            var years = curve[i + 1].Year - curve[i].Year;
            if (years <= 0) continue;
            var totalPct = (double)((curve[i + 1].PriceTnd - curve[i].PriceTnd) / curve[i + 1].PriceTnd);
            rates.Add(totalPct / years);
        }
        var annualRate = rates.Count > 0 ? Math.Clamp(rates.Average(), 0.03, 0.15) : 0.08;

        var current = (double)curve.OrderBy(p => Math.Abs(p.Year - year)).First().PriceTnd;
        var future = current * Math.Pow(1 - annualRate, Math.Max(1, horizonYears));

        return new
        {
            found = true,
            vehicule = $"{m.Brand} {m.Model} {year}",
            valeurActuelleTnd = Math.Round(current / 100) * 100,
            valeurDansNansTnd = Math.Round(future / 100) * 100,
            horizonAnnees = horizonYears,
            decoteAnnuellePct = Math.Round(annualRate * 100, 1),
            disclaimer = Disclaimer
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private record CurvePoint(
        [property: JsonPropertyName("year")] int Year,
        [property: JsonPropertyName("km")] int Km,
        [property: JsonPropertyName("priceTnd")] decimal PriceTnd);

    private async Task<List<CarMarketModel>> ActiveModelsAsync(CancellationToken ct) =>
        await _context.CarMarketModels.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);

    /// <summary>Best fuzzy match: every term must appear in brand, model OR generation.</summary>
    private async Task<CarMarketModel?> FindModelAsync(string brand, string model, CancellationToken ct)
    {
        var models = await ActiveModelsAsync(ct);
        var terms = $"{brand} {model}".ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return models.FirstOrDefault(m => terms.All(t => MatchesTerm(m, t)));
    }

    /// <summary>
    /// Generation is part of the vernacular name ("Golf 7", "Clio 4", "Picanto
    /// JA") — users and the LLM query with it, so it must be matchable too.
    /// </summary>
    private static bool MatchesTerm(CarMarketModel m, string term) =>
        m.Brand.ToLowerInvariant().Contains(term) ||
        m.Model.ToLowerInvariant().Contains(term) ||
        (m.Generation != null && m.Generation.ToLowerInvariant().Contains(term));

    private static List<CurvePoint> Curve(CarMarketModel m)
    {
        try { return JsonSerializer.Deserialize<List<CurvePoint>>(m.PriceCurveJson, JsonOpts) ?? new(); }
        catch { return new(); }
    }

    private static decimal? CheapestOption(CarMarketModel m)
    {
        var prices = Curve(m).Select(p => p.PriceTnd).ToList();
        if (m.NewPriceTnd is { } np) prices.Add(np);
        return prices.Count > 0 ? prices.Min() : null;
    }

    private static object Summary(CarMarketModel m) => new
    {
        brand = m.Brand,
        model = m.Model,
        generation = m.Generation,
        annees = $"{m.YearFrom}-{(m.YearTo?.ToString() ?? "aujourd'hui")}",
        segment = m.Segment,
        energie = m.Energy,
        moteur = m.EngineLabel,
        cvFiscaux = m.FiscalPower,
        places = m.Seats,
        prixNeufTnd = m.NewPriceTnd,
        consoReelleL100 = m.RealConsumptionL100,
        fiabiliteSur10 = m.ReliabilityScore
    };

    private static object NotCovered(string brand, string model) => new
    {
        found = false,
        message = $"« {brand} {model} » n'est pas (encore) dans la base Calypso. NE PAS inventer de prix — donner uniquement des conseils généraux et le dire à l'utilisateur.",
    };

    /// <summary>Re-embed a stored JSON array as raw JSON (not an escaped string).</summary>
    private static JsonElement Raw(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return JsonSerializer.Deserialize<JsonElement>("[]"); }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // LLMs routinely send numbers as JSON strings ("budget_tnd": "45000"), so
    // the numeric getters accept both — a silently-dropped budget/year would
    // otherwise produce misleading found:false (or worse, a wrong-year price).
    private static int? Int(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n
        : v.ValueKind == JsonValueKind.Number ? (int)v.GetDecimal()
        : v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s
        : null;

    private static decimal? Num(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Number ? v.GetDecimal()
        : v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d
        : null;
}
