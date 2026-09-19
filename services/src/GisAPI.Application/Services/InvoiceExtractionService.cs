using System.Globalization;
using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
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
    string? Category,      // fuel|maintenance|insurance|tax|toll|parking|fine|repair|other, credit_note (avoir)
    string? VehiclePlate,
    string? Description,
    string? Confidence,    // high|medium|low
    List<InvoiceLineItem>? Items = null,   // lignes de la facture (détail, best-effort)
    bool IsCreditNote = false,             // avoir fournisseur (document intitulé avoir, ou total négatif), montants rendus positifs
    // ── Carburant ────────────────────────────────────────────────────────────
    // Ajoutés en FIN de liste, optionnels : aucun appelant positionnel existant
    // n'est cassé. Un ticket de station imprime volume, prix au litre et total ;
    // l'écran Carburant a besoin des deux premiers, que l'extraction ne rendait pas.
    decimal? Liters = null,                // volume servi, en litres (null si non imprimé/illisible)
    decimal? PricePerLiter = null);        // prix au litre imprimé (null si non imprimé/illisible)

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
  ""liters"": number|null,              // CARBURANT uniquement: volume servi en litres
  ""pricePerLiter"": number|null,       // CARBURANT uniquement: prix au litre imprimé
  ""confidence"": string|null,          // high | medium | low
  ""isCreditNote"": boolean,            // true si le document s'intitule avoir (voir règle)
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
isCreditNote=true UNIQUEMENT si le document s'intitule ""Avoir"", ""Facture d'avoir"", ""Note de crédit"" ou ""Credit note"" (il rembourse ou annule une facture) ; une simple remise ne suffit pas. Recopie alors les montants tels qu'imprimés.
CARBURANT: un ticket de station imprime presque toujours le VOLUME (litres), le PRIX AU LITRE et le TOTAL. Recopie liters et pricePerLiter EXACTEMENT comme imprimés — ne les calcule pas, ne les déduis pas du total, et laisse à null celui qui n'est pas imprimé ou qui est illisible. Sur un document qui n'est pas du carburant, liters=null et pricePerLiter=null.
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

        // Complétion carburant APRÈS la mise au positif de l'avoir : elle raisonne
        // sur le total tel qu'il sera affiché. La CATÉGORIE, en revanche, est celle
        // lue sur le document : AsCreditNoteIfDetected vient de réécrire celle de
        // l'extraction en « credit_note », et un avoir de station-service resterait
        // sinon sans volume ni prix au litre.
        var positif = AsCreditNoteIfDetected(extraction);
        return new InvoiceExtractionResult(CompleteFuelVolume(positif, extraction.Category), tokens);
    }

    /// <summary>
    /// Avoir fournisseur — document intitulé avoir (<c>isCreditNote</c> lu par l'IA) OU
    /// total NÉGATIF : catégorie <c>credit_note</c> et montants en valeur absolue,
    /// lignes comprises.
    ///
    /// <para>Depuis DEF-050, POST /api/costs refuse un montant ≤ 0 : un avoir scanné
    /// arrivait à -120 dans le formulaire de revue et ne pouvait plus être enregistré
    /// (« Montant supérieur à zéro requis. »). Décision du 16/09/2026 : il s'enregistre
    /// comme le remboursement d'assurance, montant positif déduit des coûts
    /// (<see cref="VehicleCostCategory"/>).</para>
    ///
    /// <para>Beaucoup d'avoirs impriment leurs montants en POSITIF sous le titre
    /// « AVOIR » : lus sur le seul signe du total, ils partaient en DÉPENSE et
    /// gonflaient les coûts du montant que le fournisseur rendait. Une facture
    /// ordinaire au total positif ne change pas. (Public pour les tests.)</para>
    /// </summary>
    public static InvoiceExtraction AsCreditNoteIfDetected(InvoiceExtraction x)
    {
        // Même total que le formulaire de revue : TTC, à défaut HT.
        if (!x.IsCreditNote && (x.AmountTTC ?? x.AmountHT) is not < 0m) return x;

        // Les lignes ne changent de signe que si elles font un total négatif : sur un
        // avoir aux lignes imprimées en positif, les inverser les rendrait fausses.
        var items = x.Items;
        if (items is { Count: > 0 } && items.Sum(i => i.Amount ?? 0m) < 0m)
            items = items.Select(i => i with { Amount = -i.Amount }).ToList();

        return x with
        {
            AmountHT = Abs(x.AmountHT),
            AmountTVA = Abs(x.AmountTVA),
            AmountTTC = Abs(x.AmountTTC),
            Category = VehicleCostCategory.CreditNote,
            Items = items,
            IsCreditNote = true
        };

        static decimal? Abs(decimal? amount) => amount is decimal d ? Math.Abs(d) : null;
    }

    /// <summary>
    /// Tolérance d'arrondi d'un ticket de carburant : le volume est imprimé au
    /// centilitre et le prix au litre au millime, donc volume × prix ne retombe
    /// jamais au millime près sur le total imprimé. 50 millimes, ou 0,5 % du
    /// total au-delà de 10 DT (un plein de camion se compte en centaines de litres).
    /// </summary>
    private static decimal FuelTolerance(decimal total) => Math.Max(0.05m, Math.Abs(total) * 0.005m);

    /// <summary>
    /// Écart toléré entre HT + TVA et TTC pour juger un total « pur » (voir
    /// <see cref="TotalPurementCarburant"/>). Ces trois montants sont IMPRIMÉS : leur
    /// seule dérive légitime est l'arrondi au millime. 50 millimes laissent donc passer
    /// l'arrondi et arrêtent le TIMBRE FISCAL (0,600 ou 1,000 DT), qui s'ajoute au TTC
    /// sans être du carburant. Volontairement FIXE et non proportionnelle : un timbre
    /// coûte le même prix sur un ticket de 20 DT et sur un plein de camion.
    /// </summary>
    private const decimal ToleranceTotalPur = 0.05m;

    /// <summary>
    /// Le total du document peut-il se lire comme « volume × prix au litre », et rien
    /// d'autre ? Deux réserves, faute de quoi on ne déduit rien :
    /// <list type="bullet">
    ///   <item>aucune ligne facturée étrangère au carburant (lavage, huile, accessoire,
    ///     remise) : le total couvrirait alors plus que le plein. Une ligne sans catégorie
    ///     est traitée comme étrangère — on ne déduit pas d'un détail qu'on ne comprend pas ;</item>
    ///   <item>quand HT, TVA et TTC sont lus tous les trois, TTC doit valoir HT + TVA à
    ///     <see cref="ToleranceTotalPur"/> près : un écart signe un timbre fiscal ou un
    ///     poste hors carburant glissé dans le total.</item>
    /// </list>
    /// Un ticket de station ordinaire (items vide, un seul montant) passe les deux.
    /// </summary>
    private static bool TotalPurementCarburant(InvoiceExtraction x)
    {
        if (x.Items is { Count: > 0 } lignes && lignes.Any(l => !VehicleCostCategory.IsFuel(l.Category)))
            return false;

        if (x.AmountHT is decimal ht && x.AmountTVA is decimal tva && x.AmountTTC is decimal ttc)
            return Math.Abs(ht + tva - ttc) <= ToleranceTotalPur;

        return true;
    }

    /// <summary>
    /// Carburant : un ticket porte d'ordinaire le VOLUME, le PRIX AU LITRE et le
    /// TOTAL. Quand une SEULE des trois manque, elle se déduit des deux autres.
    /// Rien n'est déduit si les deux champs manquent, ni si les deux sont déjà là.
    ///
    /// <para><b>Carburant UNIQUEMENT</b> (19/09/2026). <see cref="Parse"/> accepte
    /// « volume » comme synonyme de litres : une facture de GARAGE portant « Huile
    /// 5W40 — 5 L » arrivait ici avec Liters = 5, et la complétion en tirait un prix
    /// au litre de 342,50 / 5 = 68,500 DT — un plein fantôme proposé à l'écran
    /// Carburant. La catégorie est celle LUE sur le document : <paramref name="categorieDocument"/>
    /// quand l'appelant l'a mise de côté avant que la détection d'avoir ne réécrive
    /// <see cref="InvoiceExtraction.Category"/> en « credit_note », sinon celle de l'extraction.</para>
    ///
    /// <para>Le TOTAL, lui, n'est JAMAIS déduit : c'est le montant qui partira en
    /// dépense, et le fabriquer à partir de deux chiffres peut-être mal lus
    /// inventerait de l'argent absent du document. L'écran laisse l'utilisateur le
    /// saisir.</para>
    ///
    /// <para><b>Ce que vaut la valeur rendue.</b> Elle est APPROCHÉE : ce n'est pas un
    /// chiffre lu sur le document mais un quotient arrondi (2 décimales pour un volume,
    /// 3 pour un prix au litre). Jusqu'au 19/09/2026 elle n'était gardée que « si elle
    /// retombait sur le total » — garde-fou TAUTOLOGIQUE, puisque la valeur est calculée
    /// à partir de ce même total : l'écart se réduisait à l'arrondi et le contrôle ne
    /// refusait jamais rien, pas même un total gonflé d'un timbre fiscal ou d'un article
    /// hors carburant. Il est remplacé par un contrôle qui, lui, porte : la déduction est
    /// réservée aux documents dont le total est PUR (<see cref="TotalPurementCarburant"/>).
    /// Mieux vaut un champ vide, que l'utilisateur complète en lisant son ticket, qu'un
    /// volume faux qu'il ne relira pas. (Public pour les tests.)</para>
    /// </summary>
    /// <param name="categorieDocument">Catégorie lue sur le document ; null = celle de <paramref name="x"/>.</param>
    public static InvoiceExtraction CompleteFuelVolume(InvoiceExtraction x, string? categorieDocument = null)
    {
        // Seuls les documents de carburant : ailleurs, un « 5 L » d'huile n'est pas un plein.
        if (!VehicleCostCategory.IsFuel(categorieDocument ?? x.Category)) return x;

        // Les deux présents : rien à compléter. Les deux absents : rien à déduire
        // (le ticket n'imprime ni volume ni prix au litre lisibles).
        var hasLiters = x.Liters is not null;
        var hasPrice = x.PricePerLiter is not null;
        if (hasLiters == hasPrice) return x;
        if ((x.AmountTTC ?? x.AmountHT) is not decimal total || total <= 0) return x;

        // Le total doit ne payer QUE du carburant, sinon la déduction est fausse d'autant.
        if (!TotalPurementCarburant(x)) return x;

        if (x.Liters is null && x.PricePerLiter is decimal price && price > 0)
        {
            var liters = Math.Round(total / price, 2, MidpointRounding.AwayFromZero);
            return liters > 0 ? x with { Liters = liters } : x;
        }

        if (x.PricePerLiter is null && x.Liters is decimal volume && volume > 0)
        {
            var perLiter = Math.Round(total / volume, 3, MidpointRounding.AwayFromZero);
            return perLiter > 0 ? x with { PricePerLiter = perLiter } : x;
        }

        return x;
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

        // Carburant : les trois valeurs (volume, prix au litre, total) sortent du
        // MÊME ticket ; si elles ne se recoupent pas, au moins une est mal lue —
        // la passe corrective fait relire le document plutôt que de trancher seule.
        // Le total vaut ici TTC, à défaut HT : beaucoup de tickets de station
        // n'impriment qu'un seul montant.
        if (x.Liters is decimal fuelLiters && fuelLiters > 0
            && x.PricePerLiter is decimal fuelPrice && fuelPrice > 0
            && (x.AmountTTC ?? x.AmountHT) is decimal fuelTotal && fuelTotal > 0
            && Math.Abs(fuelLiters * fuelPrice - fuelTotal) > FuelTolerance(fuelTotal))
            issues.Add($"liters ({fuelLiters}) × pricePerLiter ({fuelPrice}) = {fuelLiters * fuelPrice} ne retombe pas sur le total ({fuelTotal})");

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
            // Lignes HORS TAXE : la plupart des factures tunisiennes n'impriment qu'une
            // colonne « Total HT » par ligne, et le modèle recopie ce qui est imprimé,
            // comme on le lui demande. La somme retombe alors sur le HT, pas sur le TTC —
            // ce n'est pas une erreur de lecture. Avant (11/09/2026), ce cas déclenchait
            // une seconde analyse complète qui épuisait le plafond Groq (7 000 jetons
            // d'entrée par minute) et faisait échouer le scan suivant du client.
            var matchesHt = x.AmountHT is decimal htTotal && htTotal > 0
                            && Math.Abs(sum - htTotal) <= Math.Max(1.5m, Math.Abs(htTotal) * 0.01m);
            if (Math.Abs(sum - ttc) > tol && !matchesHt)
                issues.Add($"la somme des lignes items ({sum}) ne correspond ni à amountTTC ({ttc}) ni à amountHT ({x.AmountHT?.ToString() ?? "absent"})");
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

    /// <summary>
    /// Tolerant JSON → DTO mapping (exposed for tests).
    ///
    /// <para><b>Litres et prix au litre : CARBURANT UNIQUEMENT</b> (règle produit arrêtée
    /// par Karim le 19/09/2026 — « les litres et le prix au litre, tu les ajoutes juste à
    /// l'écran carburant »). La coupe se fait ICI, à la source, et non au moment de
    /// compléter la valeur manquante : <see cref="CompleteFuelVolume"/> empêchait bien de
    /// DÉDUIRE un prix au litre hors carburant, mais la valeur BRUTE continuait de sortir
    /// du service et l'écran Carburant la lisait. Une facture de garage portant « Huile
    /// 5W40 — 5 L » — que le modèle range volontiers sous « volume », accepté ci-dessous
    /// comme synonyme de litres — remplissait donc le volume d'un plein.</para>
    ///
    /// <para>La catégorie regardée est celle LUE sur le document, avant toute réécriture :
    /// un avoir de station-service est encore « fuel » à cet instant (c'est
    /// <see cref="AsCreditNoteIfDetected"/>, plus tard, qui le passe en « credit_note ») et
    /// garde donc son volume.</para>
    /// </summary>
    public static InvoiceExtraction Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var categorie = NormalizeCategory(Str(r, "category"));
            // Hors carburant, ni litres ni prix au litre ne sortent du service. Une catégorie
            // absente ou illisible vaut « other » : mieux vaut un champ vide, que l'utilisateur
            // complète en lisant son ticket, qu'un volume faux qu'il ne relira pas.
            var estCarburant = VehicleCostCategory.IsFuel(categorie);
            return new InvoiceExtraction(
                Str(r, "supplierName"),
                Str(r, "invoiceNumber"),
                NormalizeDate(Str(r, "date")),
                Dec(r, "amountHT"),
                Dec(r, "amountTVA"),
                Dec(r, "amountTTC"),
                Str(r, "currency"),
                categorie,
                Str(r, "vehiclePlate"),
                Str(r, "description"),
                Str(r, "confidence"),
                ParseItems(r),
                Bool(r, "isCreditNote"),
                // Le modèle nomme parfois ces deux champs en français ou « volume » ;
                // on accepte ces variantes, mais AUCUNE clé générique (unitPrice) qui,
                // sur une facture de garage, désignerait tout autre chose.
                estCarburant ? Dec(r, "liters") ?? Dec(r, "litres") ?? Dec(r, "volume") : null,
                estCarburant
                    ? Dec(r, "pricePerLiter") ?? Dec(r, "pricePerLitre") ?? Dec(r, "prixLitre") ?? Dec(r, "prixAuLitre")
                    : null);
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

    // Drapeau booléen : true/false JSON, ou chaîne « true » / « oui » renvoyée par le
    // modèle. Absent ou illisible → false : le document reste une facture ordinaire.
    private static bool Bool(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => (v.GetString() ?? "").Trim().ToLowerInvariant() is "true" or "oui" or "yes",
            _ => false
        };
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
