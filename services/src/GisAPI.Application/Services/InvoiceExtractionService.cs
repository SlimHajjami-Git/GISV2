using System.Globalization;
using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace GisAPI.Application.Services;

/// <summary>
/// Extracts the structured fields of a vehicle expense invoice (image or PDF)
/// so the user can REVIEW them before anything is saved.
///
/// Images (photos, jpg/png/webp) go to the Groq vision model; text PDFs are read
/// with PdfPig and sent to the text model. Both force a JSON-object response.
/// Nothing is persisted here — the caller returns the result for confirmation.
/// </summary>
public interface IInvoiceExtractionService
{
    Task<InvoiceExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct);
}

/// <summary>Extraction + real Groq token consumption (for the per-société quota log).</summary>
public record InvoiceExtractionResult(InvoiceExtraction Extraction, int TokensUsed);

/// <summary>One billed line of the invoice (détail) — lets the user split the
/// invoice into separate expenses, one per line.</summary>
public record InvoiceLineItem(
    string? Label,         // désignation de la ligne
    decimal? Amount,       // montant TTC de la ligne
    string? Category);     // same whitelist as InvoiceExtraction.Category, or null

/// <summary>Fields pre-filled into the expense review form (all nullable/best-effort).</summary>
public record InvoiceExtraction(
    string? SupplierName,
    string? InvoiceNumber,
    string? Date,          // ISO yyyy-MM-dd
    decimal? AmountHT,
    decimal? AmountTVA,
    decimal? AmountTTC,
    string? Currency,
    string? Category,      // fuel|maintenance|insurance|tax|toll|parking|fine|repair|other
    string? VehiclePlate,
    string? Description,
    string? Confidence,    // high|medium|low
    List<InvoiceLineItem>? Items = null);  // lignes de la facture (détail, best-effort)

public class InvoiceExtractionService : IInvoiceExtractionService
{
    private readonly ILlmService _llm;
    private readonly ILogger<InvoiceExtractionService> _logger;

    public InvoiceExtractionService(ILlmService llm, ILogger<InvoiceExtractionService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    private const string SystemPrompt = @"Tu extrais les données d'une FACTURE liée à un véhicule (parc automobile tunisien).
Réponds UNIQUEMENT par un objet JSON avec exactement ces clés :
{
  ""supplierName"": string|null,        // nom du fournisseur / garage / station
  ""invoiceNumber"": string|null,       // numéro de facture
  ""date"": string|null,                // date de la facture au format YYYY-MM-DD
  ""amountHT"": number|null,            // montant hors taxe
  ""amountTVA"": number|null,           // montant de la TVA
  ""amountTTC"": number|null,           // montant total TTC (le plus important)
  ""currency"": string|null,            // code devise, ex: TND (défaut si non précisé)
  ""category"": string|null,            // UNE parmi: fuel, maintenance, insurance, tax, toll, parking, fine, repair, other
  ""vehiclePlate"": string|null,        // immatriculation si présente, ex: 123 TU 4567
  ""description"": string|null,         // résumé court des biens/prestations
  ""confidence"": string|null,          // high | medium | low
  ""items"": [                          // DÉTAIL: chaque ligne facturée (article/prestation)
    { ""label"": string,                // désignation de la ligne, ex: ""Vidange moteur""
      ""amount"": number|null,          // montant TTC de la ligne
      ""category"": string|null }       // même liste que category, la plus adaptée à CETTE ligne
  ]
}
Règles: les montants sont des NOMBRES (pas de texte, pas de symbole). Utilise null si une valeur est absente ou illisible.
PRÉCISION AVANT TOUT: recopie chaque nombre EXACTEMENT comme imprimé sur le document (mêmes chiffres, mêmes décimales) — ne recalcule pas, n'arrondis pas, ne devine pas. Un chiffre douteux/flou = null. Il vaut mieux un champ null qu'un champ faux.
amountTTC = la ligne ""TOTAL TTC"" / ""NET À PAYER"" du document (pas ta propre addition).
Choisis la catégorie la plus probable d'après le contenu (carburant→fuel, entretien/vidange→maintenance, réparation→repair, assurance→insurance, vignette→tax, péage→toll, parking→parking, amende→fine, sinon other).
Pour items: liste les lignes réellement facturées (désignation + montant TTC ligne), dans l'ordre du document. Vérifie que la somme des lignes est cohérente avec le total — si elle ne l'est pas, re-lis le document avant de répondre.
N'INVENTE JAMAIS de ligne — si le détail est absent ou illisible, renvoie items=[]. Ignore les sous-totaux, remises globales et lignes de TVA.
Si le document n'est pas une facture, mets confidence=""low"", items=[] et les champs à null.
Réponds UNIQUEMENT avec le JSON, sans texte autour, en gardant chaque désignation courte (max 60 caractères).";

    public async Task<InvoiceExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                      || ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
        var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || ext == ".pdf";

        LlmResponse response;
        if (isImage)
        {
            var mime = contentType.StartsWith("image/") ? contentType : "image/jpeg";
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(content)}";
            // 2500 tokens: the items array made 1024 too small — a truncated
            // response is invalid JSON, and Parse() then degrades to an all-null
            // low-confidence result (seen in prod as "aucun champ extrait").
            response = await _llm.ExtractJsonAsync(SystemPrompt,
                "Analyse cette facture et renvoie le JSON demandé.", dataUrl, 2500, ct);
        }
        else if (isPdf)
        {
            var text = ExtractPdfText(content);
            if (text.Trim().Length < 30)
                throw new InvalidOperationException(
                    "PDF probablement scanné (aucun texte lisible). Merci d'envoyer une photo ou une image de la facture.");
            if (text.Length > 12000) text = text[..12000];
            response = await _llm.ExtractJsonAsync(SystemPrompt,
                "Voici le texte extrait d'une facture. Renvoie le JSON demandé.\n\n" + text, null, 2500, ct);
        }
        else
        {
            throw new InvalidOperationException("Format non supporté. Envoyez une image (JPG/PNG) ou un PDF.");
        }

        return new InvoiceExtractionResult(Parse(response.Content), response.TokensUsed);
    }

    private static string ExtractPdfText(byte[] content)
    {
        using var pdf = PdfDocument.Open(content);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    /// <summary>Tolerant JSON → DTO mapping (exposed for tests).</summary>
    public static InvoiceExtraction Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new InvoiceExtraction(
                Str(r, "supplierName"),
                Str(r, "invoiceNumber"),
                NormalizeDate(Str(r, "date")),
                Dec(r, "amountHT"),
                Dec(r, "amountTVA"),
                Dec(r, "amountTTC"),
                Str(r, "currency"),
                NormalizeCategory(Str(r, "category")),
                Str(r, "vehiclePlate"),
                Str(r, "description"),
                Str(r, "confidence"),
                ParseItems(r));
        }
        catch
        {
            return new InvoiceExtraction(null, null, null, null, null, null, null, null, null, null, "low", new List<InvoiceLineItem>());
        }
    }

    /// <summary>Tolerant mapping of the "items" array — skips junk entries, caps the
    /// count, accepts a few alternative key names the LLM occasionally produces.</summary>
    private static List<InvoiceLineItem> ParseItems(JsonElement root)
    {
        var list = new List<InvoiceLineItem>();
        if (!root.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var it in arr.EnumerateArray())
        {
            if (it.ValueKind != JsonValueKind.Object) continue;
            var label = Str(it, "label") ?? Str(it, "designation") ?? Str(it, "description");
            var amount = Dec(it, "amount") ?? Dec(it, "amountTTC") ?? Dec(it, "total") ?? Dec(it, "price");
            if (label is null && amount is null) continue;   // junk line

            var rawCat = Str(it, "category");
            list.Add(new InvoiceLineItem(label, amount, rawCat is null ? null : NormalizeCategory(rawCat)));
            if (list.Count >= 30) break;                     // hard cap — a facture never has more
        }
        return list;
    }

    private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
        { "fuel", "maintenance", "insurance", "tax", "toll", "parking", "fine", "repair", "other" };

    private static string NormalizeCategory(string? c) =>
        !string.IsNullOrWhiteSpace(c) && Categories.Contains(c.Trim()) ? c.Trim().ToLowerInvariant() : "other";

    private static string? NormalizeDate(string? d)
    {
        if (string.IsNullOrWhiteSpace(d)) return null;
        d = d.Trim();
        foreach (var fmt in new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "dd.MM.yyyy", "MM/dd/yyyy" })
            if (DateTime.TryParseExact(d, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
        return DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any)
            ? any.ToString("yyyy-MM-dd") : null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString())
            : null;

    // Amounts: accept a JSON number or a stringified number ("1 234,560 DT").
    private static decimal? Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = new string((v.GetString() ?? "").Where(ch => char.IsDigit(ch) || ch is '.' or ',').ToArray());
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Contains('.') && s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
            else s = s.Replace(',', '.');
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
        }
        return null;
    }
}
