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
/// with PdfPig and sent to the text model; SCANNED PDFs (no text layer) fall back
/// to the largest embedded image of the first pages, sent to the vision model.
/// After extraction, the amounts are checked for coherence (HT+TVA≈TTC, somme des
/// lignes≈TTC) and ONE corrective pass is replayed when they don't add up — the
/// confidence is downgraded if the incoherence persists.
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
    decimal? Amount,       // montant TTC de la ligne (négatif pour une remise)
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

    private const string SystemPrompt = @"Tu extrais les données d'une FACTURE ou d'un TICKET DE CAISSE lié à un véhicule (parc automobile tunisien).
Le document peut être une facture formelle, un reçu de station-service, un ticket de caisse, une quittance d'assurance — imprimé ou partiellement manuscrit, en français et/ou en arabe.
Réponds UNIQUEMENT par un objet JSON avec exactement ces clés :
{
  ""supplierName"": string|null,        // nom du fournisseur / garage / station
  ""invoiceNumber"": string|null,       // numéro de facture ou de ticket
  ""date"": string|null,                // date du document au format YYYY-MM-DD
  ""amountHT"": number|null,            // montant hors taxe
  ""amountTVA"": number|null,           // montant de la TVA
  ""amountTTC"": number|null,           // montant total TTC (le plus important)
  ""currency"": string|null,            // code devise, ex: TND (défaut si non précisé)
  ""category"": string|null,            // UNE parmi: fuel, maintenance, insurance, tax, toll, parking, fine, repair, other
  ""vehiclePlate"": string|null,        // immatriculation si présente
  ""description"": string|null,         // résumé court des biens/prestations
  ""confidence"": string|null,          // high | medium | low
  ""items"": [                          // DÉTAIL: chaque ligne facturée (article/prestation)
    { ""label"": string,                // désignation de la ligne, ex: ""Vidange moteur""
      ""amount"": number|null,          // montant TTC de la ligne (NÉGATIF pour une remise)
      ""category"": string|null }       // même liste que category, la plus adaptée à CETTE ligne
  ]
}
Règles: les montants sont des NOMBRES (pas de texte, pas de symbole). Utilise null si une valeur est absente ou illisible.
PRÉCISION AVANT TOUT: recopie chaque nombre EXACTEMENT comme imprimé sur le document (mêmes chiffres, mêmes décimales) — ne recalcule pas, n'arrondis pas, ne devine pas. Un chiffre douteux/flou = null. Il vaut mieux un champ null qu'un champ faux.
En Tunisie les montants ont souvent 3 DÉCIMALES (millimes, ex: 128,540 DT) — recopie les trois décimales telles quelles ; ne confonds pas le séparateur de milliers avec la virgule décimale.
amountTTC = la ligne ""TOTAL TTC"" / ""NET À PAYER"" / ""TOTAL"" du document (pas ta propre addition).
Le TIMBRE FISCAL (souvent 0,600 ou 1,000 DT) fait partie du TTC mais N'EST PAS une ligne d'article : ne le mets pas dans items (TTC peut donc valoir HT + TVA + timbre).
Si une REMISE GLOBALE figure sur le document, ajoute-la dans items comme ligne ""Remise"" avec un montant NÉGATIF (ainsi la somme des lignes reste égale au total).
Immatriculation tunisienne: formats ""123 TUN 4567"" / ""123 تونس 4567"" / régime spécial (RS, TRAC...). Si elle est écrite en arabe, translittère en ""123 TUN 4567"".
Choisis la catégorie la plus probable d'après le contenu (carburant/gasoil/essence→fuel, entretien/vidange→maintenance, réparation/pièces→repair, assurance→insurance, vignette/taxe→tax, péage→toll, parking→parking, amende→fine, sinon other).
Pour items: liste les lignes réellement facturées (désignation + montant TTC ligne), dans l'ordre du document. Vérifie que la somme des lignes (remises comprises) est cohérente avec le total — si elle ne l'est pas, re-lis le document avant de répondre.
N'INVENTE JAMAIS de ligne — si le détail est absent ou illisible, renvoie items=[]. Ignore les sous-totaux et les lignes de TVA.
Si le document n'est pas une facture ni un reçu, mets confidence=""low"", items=[] et les champs à null.
Réponds UNIQUEMENT avec le JSON, sans texte autour, en gardant chaque désignation courte (max 60 caractères).";

    public async Task<InvoiceExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                      || ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif";
        var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) || ext == ".pdf";

        string userText;
        string? dataUrl = null;

        if (isImage)
        {
            var mime = contentType.StartsWith("image/") ? contentType : "image/jpeg";
            dataUrl = $"data:{mime};base64,{Convert.ToBase64String(content)}";
            userText = "Analyse cette facture et renvoie le JSON demandé.";
        }
        else if (isPdf)
        {
            var text = ExtractPdfText(content);
            if (text.Trim().Length >= 30)
            {
                if (text.Length > 12000) text = text[..12000];
                userText = "Voici le texte extrait d'une facture. Renvoie le JSON demandé.\n\n" + text;
            }
            else
            {
                // PDF scanné (aucune couche texte) : la page est en général UNE grande
                // image intégrée — on l'extrait et on passe par le modèle vision au
                // lieu de rejeter le fichier.
                var img = ExtractLargestPdfImage(content);
                if (img is null)
                    throw new InvalidOperationException(
                        "PDF scanné illisible (aucun texte ni image exploitable). Merci d'envoyer une photo ou une image de la facture.");
                dataUrl = $"data:{img.Value.Mime};base64,{Convert.ToBase64String(img.Value.Bytes)}";
                userText = "Analyse cette facture et renvoie le JSON demandé.";
            }
        }
        else
        {
            throw new InvalidOperationException("Format non supporté. Envoyez une image (JPG/PNG) ou un PDF.");
        }

        // 2500 tokens: the items array made 1024 too small — a truncated
        // response is invalid JSON, and Parse() then degrades to an all-null
        // low-confidence result (seen in prod as "aucun champ extrait").
        var response = await _llm.ExtractJsonAsync(SystemPrompt, userText, dataUrl, 2500, ct);
        var extraction = Parse(response.Content);
        var tokens = response.TokensUsed;

        // Contrôle de cohérence des montants + UNE passe corrective. Le modèle
        // relit le document avec ses propres erreurs sous les yeux — c'est la
        // parade la plus efficace contre les chiffres mal lus (flou, millimes).
        var issues = CoherenceIssues(extraction);
        if (issues.Count > 0)
        {
            _logger.LogInformation("Invoice scan incoherent ({Issues}) — corrective pass", string.Join(" | ", issues));
            try
            {
                var fixText = userText
                    + "\n\nTa première extraction était :\n" + response.Content
                    + "\n\nElle contient ces incohérences :\n- " + string.Join("\n- ", issues)
                    + "\nRelis le document chiffre par chiffre (attention aux 3 décimales/millimes, au timbre fiscal et aux remises) et renvoie le JSON COMPLET corrigé, même format.";
                var second = await _llm.ExtractJsonAsync(SystemPrompt, fixText, dataUrl, 2500, ct);
                tokens += second.TokensUsed;
                var corrected = Parse(second.Content);
                if (CoherenceIssues(corrected).Count < issues.Count && corrected.AmountTTC is not null)
                    extraction = corrected;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Corrective extraction pass failed — keeping first result");
            }

            // Toujours incohérent → on le dit à l'utilisateur via la confiance.
            if (CoherenceIssues(extraction).Count > 0 &&
                string.Equals(extraction.Confidence, "high", StringComparison.OrdinalIgnoreCase))
                extraction = extraction with { Confidence = "medium" };
        }

        return new InvoiceExtractionResult(extraction, tokens);
    }

    /// <summary>
    /// Incohérences arithmétiques détectables sans revoir le document.
    /// Tolérances adaptées aux factures tunisiennes : le timbre fiscal (~1 DT)
    /// s'ajoute à HT+TVA, et les arrondis de millimes existent — on ne signale
    /// que les écarts supérieurs à 1,5 DT ET 1 % du total.
    /// (Public pour les tests.)
    /// </summary>
    public static List<string> CoherenceIssues(InvoiceExtraction x)
    {
        var issues = new List<string>();
        if (x.AmountTTC is not decimal ttc || ttc <= 0) return issues;
        var tol = Math.Max(1.5m, Math.Abs(ttc) * 0.01m);

        if (x.AmountHT is decimal ht && x.AmountTVA is decimal tva)
        {
            var diff = Math.Abs(ht + tva - ttc);
            if (diff > tol)
                issues.Add($"amountHT ({ht}) + amountTVA ({tva}) = {ht + tva} est loin de amountTTC ({ttc})");
        }

        var items = x.Items;
        if (items is { Count: > 0 } && items.All(i => i.Amount.HasValue))
        {
            var sum = items.Sum(i => i.Amount!.Value);
            if (Math.Abs(sum - ttc) > tol)
                issues.Add($"la somme des lignes items ({sum}) ne correspond pas à amountTTC ({ttc})");
        }
        return issues;
    }

    private static string ExtractPdfText(byte[] content)
    {
        using var pdf = PdfDocument.Open(content);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    /// <summary>
    /// Plus grande image intégrée des 3 premières pages d'un PDF scanné
    /// (PNG via PdfPig quand décodable, sinon flux JPEG brut). Null si rien
    /// d'exploitable (&lt; 10 Ko = logos/filigranes, pas une page scannée).
    /// </summary>
    private static (byte[] Bytes, string Mime)? ExtractLargestPdfImage(byte[] content)
    {
        try
        {
            using var pdf = PdfDocument.Open(content);
            (byte[] Bytes, string Mime)? best = null;
            foreach (var page in pdf.GetPages().Take(3))
            {
                foreach (var img in page.GetImages())
                {
                    byte[]? bytes = null; string mime = "image/png";
                    try { if (img.TryGetPng(out var png)) bytes = png; } catch { /* format non décodable */ }
                    if (bytes is null)
                    {
                        var raw = img.RawBytes.ToArray();
                        // Flux DCTDecode = JPEG prêt à l'emploi (signature FF D8 FF).
                        if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF)
                        { bytes = raw; mime = "image/jpeg"; }
                    }
                    if (bytes is { Length: > 10_000 } && (best is null || bytes.Length > best.Value.Bytes.Length))
                        best = (bytes, mime);
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
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

    // Amounts: accept a JSON number or a stringified number ("1 234,560 DT",
    // "-12,000" pour une remise — le signe est conservé).
    private static decimal? Dec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String)
        {
            var raw = (v.GetString() ?? "").Trim();
            var negative = raw.StartsWith('-') || raw.StartsWith('(');
            var s = new string(raw.Where(ch => char.IsDigit(ch) || ch is '.' or ',').ToArray());
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Contains('.') && s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
            else s = s.Replace(',', '.');
            if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return null;
            return negative ? -d : d;
        }
        return null;
    }
}
