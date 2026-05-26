using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// LLM-assisted accident analysis. The deterministic engine in
/// <see cref="AccidentDetectionService"/> already produces a faithful
/// story / indicators / synthesis from the raw signal — this service
/// OPTIONALLY upgrades the qualitative prose (synthesis sentence + the
/// "reasons" that justify the diagnosis) by feeding the actual telemetry
/// frames (before / during / after the shock) to Groq.
///
/// <para>
/// Hard guarantees so an LLM never corrupts a quasi-legal document:
/// <list type="bullet">
///   <item>Exact numbers (times, speeds, magnitude) stay deterministic —
///   the LLM only writes prose, never the indicators table.</item>
///   <item>A strict system prompt forbids inventing causes / weather /
///   third parties / locations.</item>
///   <item>A numeric guardrail rejects any output whose km/h figures don't
///   match a real frame speed.</item>
///   <item>Best-effort: ANY failure (no key, timeout, bad JSON, guardrail)
///   returns <c>null</c> and the caller keeps the deterministic narrative.</item>
/// </list>
/// </para>
/// </summary>
public interface IAccidentNarrativeService
{
    Task<AccidentNarrative?> TryGenerateAsync(
        GisDbContext context,
        AccidentCandidate candidate,
        string vehicleLabel,
        GeocodedLocation? location,
        CancellationToken ct);
}

/// <summary>LLM-written prose. Story + indicators remain deterministic.</summary>
public record AccidentNarrative(string SynthesisText, List<AccidentNarrativeReason> Reasons);

public record AccidentNarrativeReason(string Title, string Text);

public class AccidentNarrativeService : IAccidentNarrativeService
{
    private readonly ILlmService _llm;
    private readonly ILogger<AccidentNarrativeService> _logger;

    /// <summary>
    /// Bounds the LLM call so a slow Groq response can't stall the 2-min
    /// detection scan. On timeout we simply keep the deterministic narrative.
    /// </summary>
    private static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(25);

    private const string SystemPrompt =
        "Tu es un expert d'assurance qui rédige le rapport d'un accident de véhicule à partir des données du boîtier embarqué. " +
        "On te fournit des éléments déjà analysés (intensité, direction du choc, vitesses, durées) plus la liste des relevés " +
        "enregistrés avant, pendant et après l'événement.\n\n" +
        "Ta mission : rédiger une SYNTHÈSE factuelle et des MOTIFS qui justifient le diagnostic, " +
        "en t'appuyant UNIQUEMENT sur les données fournies.\n\n" +
        "VOCABULAIRE OBLIGATOIRE — Le rapport est destiné à un client (gestionnaire de flotte ou expert d'assurance), " +
        "PAS à un ingénieur. Tu DOIS utiliser exclusivement le vocabulaire métier suivant :\n" +
        "- Direction du choc : « frontal », « latéral », « frontal et latéral », « vertical », « multi-direction », « retournement ».\n" +
        "- Intensité du choc : « très violent », « violent », « modéré », « faible ».\n" +
        "- Position : « inclinée », « retournée », « basculée sur le flanc ».\n" +
        "- Mouvement : « immobilisé », « chute brutale de la vitesse », « dépanneuse ».\n\n" +
        "VOCABULAIRE INTERDIT — Tu ne dois JAMAIS écrire :\n" +
        "- « MEMS », « accéléromètre », « capteur d'accélération » → remplacer par « capteurs embarqués » ou ne pas mentionner du tout.\n" +
        "- « axe X », « axe Y », « axe Z », « |X|=... », « |Y|=... », « |Z|=... » → traduire en direction métier (frontal / latéral / vertical).\n" +
        "- « magnitude N », « somme vectorielle », « /222 », « saturé à 127 », « ±127 » → traduire en intensité qualitative.\n" +
        "- Toute référence à des unités, échelles ou index techniques internes.\n\n" +
        "RÈGLES ABSOLUES :\n" +
        "- N'invente JAMAIS un fait absent des données : aucune cause non démontrée " +
        "(ex. « le conducteur s'est endormi »), aucune météo, aucun tiers, aucun lieu autre que celui fourni.\n" +
        "- Utilise uniquement les vitesses, heures et durées fournies. Ne fabrique AUCUN chiffre.\n" +
        "- Si une information n'est pas dans les données, ne la mentionne pas.\n" +
        "- Reste sobre, professionnel et factuel.\n" +
        "- Réponds STRICTEMENT en JSON valide, sans aucun texte hors du JSON, au format exact :\n" +
        "{\"synthesisText\": \"...\", \"reasons\": [{\"title\": \"...\", \"text\": \"...\"}]}\n" +
        "- synthesisText : 1 à 3 phrases décrivant ce qui s'est passé en langage client. " +
        "reasons : 3 à 4 observations concordantes, chacune avec un titre court et une explication chiffrée en km/h ou minutes.";

    public AccidentNarrativeService(ILlmService llm, ILogger<AccidentNarrativeService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<AccidentNarrative?> TryGenerateAsync(
        GisDbContext context,
        AccidentCandidate candidate,
        string vehicleLabel,
        GeocodedLocation? location,
        CancellationToken ct)
    {
        try
        {
            var frames = await LoadFramesAsync(context, candidate, ct);
            if (frames.Count < 5)
            {
                _logger.LogDebug(
                    "AccidentNarrativeService: only {Count} frames around impact — skipping LLM enrichment",
                    frames.Count);
                return null;
            }

            var kept = Downsample(frames, candidate.RecordedAt);
            var user = BuildUserPrompt(candidate, vehicleLabel, location, kept);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(LlmTimeout);

            var resp = await _llm.ChatAsync(
                SystemPrompt,
                new List<LlmMessage> { new("user", user) },
                maxTokens: 900,
                ct: cts.Token);

            var realSpeeds = frames
                .Select(f => Math.Round(f.SpeedKph))
                .Append(Math.Round(candidate.KphBef))
                .Append(Math.Round(candidate.KphAft))
                .Distinct()
                .ToList();

            var narrative = ParseAndValidate(resp.Content, realSpeeds);
            if (narrative == null)
            {
                _logger.LogWarning(
                    "AccidentNarrativeService: LLM output rejected (parse or numeric guardrail) — keeping deterministic narrative");
                return null;
            }

            _logger.LogInformation(
                "AccidentNarrativeService: LLM narrative accepted ({Tokens} tokens) for device {DeviceId} at {At}",
                resp.TokensUsed, candidate.DeviceId, candidate.RecordedAt);
            return narrative;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AccidentNarrativeService: LLM call timed out after {Timeout}s — keeping deterministic narrative",
                LlmTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AccidentNarrativeService: enrichment failed — keeping deterministic narrative");
            return null;
        }
    }

    // ── Evidence extraction ────────────────────────────────────────────────

    private static async Task<List<AccidentFrame>> LoadFramesAsync(
        GisDbContext context, AccidentCandidate c, CancellationToken ct)
    {
        var from = c.RecordedAt.AddMinutes(-6);
        var to = c.RecordedAt.AddMinutes(12);

        const string sql = @"
SELECT
    recorded_at AS ""RecordedAt"",
    COALESCE(speed_kph, 0)::double precision AS ""SpeedKph"",
    ""MemsX""::int AS ""MemsX"",
    ""MemsY""::int AS ""MemsY"",
    ""MemsZ""::int AS ""MemsZ"",
    course_deg AS ""CourseDeg"",
    ignition_on AS ""IgnitionOn""
FROM gps_positions
WHERE device_id = {0}
  AND recorded_at >= {1}
  AND recorded_at <= {2}
  AND is_valid = TRUE
ORDER BY recorded_at;
";
        return await context.Database
            .SqlQueryRaw<AccidentFrame>(sql, c.DeviceId, from, to)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Keep full resolution within ±90 s of impact (the decisive window) and
    /// thin out the rest to ~1 frame / 30 s so the prompt stays compact.
    /// </summary>
    private static List<AccidentFrame> Downsample(List<AccidentFrame> frames, DateTime impact)
    {
        var kept = new List<AccidentFrame>();
        AccidentFrame? lastFar = null;
        foreach (var f in frames.OrderBy(f => f.RecordedAt))
        {
            var dt = Math.Abs((f.RecordedAt - impact).TotalSeconds);
            if (dt <= 90)
            {
                kept.Add(f);
            }
            else if (lastFar == null || Math.Abs((f.RecordedAt - lastFar.RecordedAt).TotalSeconds) >= 30)
            {
                kept.Add(f);
                lastFar = f;
            }
        }
        return kept;
    }

    private static string BuildUserPrompt(
        AccidentCandidate c, string vehicleLabel, GeocodedLocation? loc, List<AccidentFrame> frames)
    {
        // Pre-translate the raw signal into the client vocabulary so the
        // LLM never sees axis triplets or magnitude indices in its input,
        // only the qualitative labels it is required to reuse in output.
        var direction = AccidentNarrativeBuilder.DetectDirection(c);
        var intensity = AccidentNarrativeBuilder.DetectIntensity(c);
        var directionLabel = AccidentNarrativeBuilder.DirectionLabel(direction) ?? "non déterminée";
        var intensityLabel = AccidentNarrativeBuilder.IntensityLabel(intensity);

        // Frames are reduced to the things the client cares about: time
        // relative to impact, speed, ignition. No axis triplets — the
        // direction is already decided above and the LLM should not be
        // tempted to mention raw axes.
        var sb = new StringBuilder();
        foreach (var f in frames)
        {
            var rel = (int)Math.Round((f.RecordedAt - c.RecordedAt).TotalSeconds);
            sb.Append('t').Append(rel >= 0 ? "+" : "").Append(rel).Append("s : ");
            sb.Append("vitesse = ").Append(Math.Round(f.SpeedKph)).Append(" km/h");
            if (f.IgnitionOn.HasValue)
            {
                sb.Append(", contact ").Append(f.IgnitionOn.Value ? "allumé" : "coupé");
            }
            sb.AppendLine();
        }

        var locStr = loc == null
            ? "inconnu"
            : string.Join(", ", new[] { loc.Commune, loc.Governorate, loc.Road }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(locStr)) locStr = "inconnu";

        var tiltLine = c.TiltDurationMin >= 2
            ? $"- Position anormalement inclinée maintenue {c.TiltDurationMin} min après l'arrêt (retournement probable)\n"
            : "";
        var secondShockLine = c.SecondShockMag >= 100
            ? "- Un second choc distinct a été enregistré dans les secondes suivant l'impact principal\n"
            : "";
        var towLine = c.HasTow
            ? "- Le véhicule a ensuite été déplacé par un mouvement compatible avec une dépanneuse\n"
            : "";
        var priorLine = c.NPrior7d == 0
            ? "- Aucun événement comparable n'a été enregistré sur ce véhicule au cours des 7 derniers jours\n"
            : $"- {c.NPrior7d} événement(s) d'intensité comparable enregistré(s) au cours des 7 derniers jours\n";

        return
            "Tu rédiges le rapport d'un accident de véhicule.\n\n" +
            $"Véhicule : {vehicleLabel}\n" +
            $"Heure de l'impact (UTC) : {c.RecordedAt:yyyy-MM-dd HH:mm:ss}\n" +
            $"Lieu : {locStr}\n\n" +
            "Éléments d'analyse à reprendre tels quels (vocabulaire client obligatoire) :\n" +
            $"- Direction du choc : {directionLabel}\n" +
            $"- Intensité du choc : {intensityLabel}\n" +
            $"- Vitesse de croisière avant l'impact : {Math.Round(c.KphBef)} km/h\n" +
            $"- Vitesse maximale après l'impact : {Math.Round(c.KphAft)} km/h\n" +
            tiltLine +
            secondShockLine +
            towLine +
            priorLine +
            "\nRelevés du véhicule autour de l'événement :\n" +
            sb +
            "\nRédige la synthèse et les motifs en respectant le vocabulaire client obligatoire (frontal / latéral / violent / retournement / etc.) " +
            "et le format JSON. Tu DOIS reprendre la direction et l'intensité telles qu'elles te sont données — ne pas en inventer d'autres, " +
            "ne JAMAIS citer d'axe X/Y/Z, de magnitude numérique, de capteur MEMS ou d'index technique.";
    }

    // ── Parsing + guardrail ────────────────────────────────────────────────

    private static AccidentNarrative? ParseAndValidate(string content, List<double> realSpeeds)
    {
        var json = ExtractJson(content);
        if (json == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var synth = root.TryGetProperty("synthesisText", out var s) ? s.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(synth)) return null;

            var reasons = new List<AccidentNarrativeReason>();
            if (root.TryGetProperty("reasons", out var rs) && rs.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in rs.EnumerateArray())
                {
                    var title = r.TryGetProperty("title", out var t) ? t.GetString()?.Trim() : null;
                    var text = r.TryGetProperty("text", out var x) ? x.GetString()?.Trim() : null;
                    if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(text))
                        reasons.Add(new AccidentNarrativeReason(title!, text!));
                }
            }
            if (reasons.Count is < 1 or > 6) return null;

            var prose = synth + " " + string.Join(" ", reasons.Select(r => r.Text));
            if (!SpeedsAreFaithful(prose, realSpeeds)) return null;

            return new AccidentNarrative(synth!, reasons);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Pulls the JSON object out of the model reply, tolerating ```json fences
    /// or leading prose the model sometimes adds despite instructions.
    /// </summary>
    private static string? ExtractJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return content.Substring(start, end - start + 1);
    }

    /// <summary>
    /// Every explicit "NN km/h" figure in the prose must be within ±6 of a
    /// real frame speed (0–5 km/h is always allowed = immobilisation). This is
    /// the anti-hallucination net: a fabricated speed gets the whole output
    /// rejected, falling back to the deterministic narrative.
    /// </summary>
    private static bool SpeedsAreFaithful(string prose, List<double> realSpeeds)
    {
        foreach (Match m in Regex.Matches(prose, @"(\d+(?:[.,]\d+)?)\s*km\s*/?\s*h", RegexOptions.IgnoreCase))
        {
            if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                continue;
            if (v <= 5) continue; // immobilisation / near-stop, always plausible
            if (!realSpeeds.Any(a => Math.Abs(a - v) <= 6)) return false;
        }
        return true;
    }
}

/// <summary>
/// Flat row for <c>SqlQueryRaw</c> — one telemetry frame around the impact.
/// Property names map to the quoted SQL aliases.
/// </summary>
public class AccidentFrame
{
    public DateTime RecordedAt { get; set; }
    public double SpeedKph { get; set; }
    public int? MemsX { get; set; }
    public int? MemsY { get; set; }
    public int? MemsZ { get; set; }
    public double? CourseDeg { get; set; }
    public bool? IgnitionOn { get; set; }
}
