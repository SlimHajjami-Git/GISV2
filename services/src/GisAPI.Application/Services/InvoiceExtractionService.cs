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
    Task<InvoiceExtraction> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct);
}

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
    string? Confidence);   // high|medium|low

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
  ""confidence"": string|null           // high | medium | low
}
Règles: les montants sont des NOMBRES (pas de texte, pas de symbole). Utilise null si une valeur est absente ou illisible.
Choisis la catégorie la plus probable d'après le contenu (carburant→fuel, entretien/vidange→maintenance, réparation→repair, assurance→insurance, vignette→tax, péage→toll, parking→parking, amende→fine, sinon other).
Si le document n'est pas une facture, mets confidence=""low"" et les champs à null.";

    public async Task<InvoiceExtraction> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct)
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
            response = await _llm.ExtractJsonAsync(SystemPrompt,
                "Analyse cette facture et renvoie le JSON demandé.", dataUrl, 1024, ct);
        }
        else if (isPdf)
        {
            var text = ExtractPdfText(content);
            if (text.Trim().Length < 30)
                throw new InvalidOperationException(
                    "PDF probablement scanné (aucun texte lisible). Merci d'envoyer une photo ou une image de la facture.");
            if (text.Length > 12000) text = text[..12000];
            response = await _llm.ExtractJsonAsync(SystemPrompt,
                "Voici le texte extrait d'une facture. Renvoie le JSON demandé.\n\n" + text, null, 1024, ct);
        }
        else
        {
            throw new InvalidOperationException("Format non supporté. Envoyez une image (JPG/PNG) ou un PDF.");
        }

        return Parse(response.Content);
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
                Str(r, "confidence"));
        }
        catch
        {
            return new InvoiceExtraction(null, null, null, null, null, null, null, null, null, null, "low");
        }
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
