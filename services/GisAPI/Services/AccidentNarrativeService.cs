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
        "Tu es un expert en analyse d'accidents de véhicules à partir de données télématiques " +
        "(GPS + accéléromètre MEMS). On te fournit les trames brutes enregistrées par le boîtier " +
        "AVANT, PENDANT et APRÈS un choc détecté, plus des chiffres de référence déjà calculés.\n\n" +
        "Ta mission : rédiger une SYNTHÈSE factuelle et des MOTIFS qui justifient le diagnostic, " +
        "en t'appuyant UNIQUEMENT sur les données fournies.\n\n" +
        "RÈGLES ABSOLUES :\n" +
        "- N'invente JAMAIS un fait absent des données : aucune cause non démontrée " +
        "(ex. « le conducteur s'est endormi »), aucune météo, aucun tiers, aucun lieu autre que celui fourni.\n" +
        "- Utilise uniquement les vitesses, heures et magnitudes fournies. Ne fabrique AUCUN chiffre.\n" +
        "- Si une information n'est pas dans les données, ne la mentionne pas.\n" +
        "- Reste sobre, professionnel et factuel : ce document peut être lu par un expert d'assurance.\n" +
        "- Repères MEMS : valeurs bornées à ±127 (saturation d'un axe). |X| et |Y| élevés simultanément = " +
        "choc horizontal violent ; |Z| élevé et soutenu après l'arrêt = possible retournement.\n" +
        "- Réponds STRICTEMENT en JSON valide, sans aucun texte hors du JSON, au format exact :\n" +
        "{\"synthesisText\": \"...\", \"reasons\": [{\"title\": \"...\", \"text\": \"...\"}]}\n" +
        "- synthesisText : 1 à 3 phrases décrivant ce qui s'est passé. " +
        "reasons : 3 à 4 observations concordantes, chacune avec un titre court et une explication chiffrée.";

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
        var sb = new StringBuilder();
        foreach (var f in frames)
        {
            var rel = (int)Math.Round((f.RecordedAt - c.RecordedAt).TotalSeconds);
            sb.Append('t').Append(rel >= 0 ? "+" : "").Append(rel).Append("s ");
            sb.Append("v=").Append(Math.Round(f.SpeedKph)).Append("kph ");
            if (f.MemsX.HasValue || f.MemsY.HasValue || f.MemsZ.HasValue)
            {
                sb.Append("mems=")
                  .Append(Clamp(f.MemsX)).Append('/')
                  .Append(Clamp(f.MemsY)).Append('/')
                  .Append(Clamp(f.MemsZ)).Append(' ');
            }
            if (f.CourseDeg.HasValue) sb.Append("cap=").Append(Math.Round(f.CourseDeg.Value)).Append("° ");
            if (f.IgnitionOn.HasValue) sb.Append("ign=").Append(f.IgnitionOn.Value ? "on" : "off");
            sb.AppendLine();
        }

        var locStr = loc == null
            ? "inconnu"
            : string.Join(", ", new[] { loc.Commune, loc.Governorate, loc.Road }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        if (string.IsNullOrWhiteSpace(locStr)) locStr = "inconnu";

        return
            "Voici les données télématiques d'un choc détecté.\n\n" +
            $"Véhicule : {vehicleLabel}\n" +
            $"Heure de l'impact (UTC) : {c.RecordedAt:yyyy-MM-dd HH:mm:ss}\n" +
            $"Lieu : {locStr}\n\n" +
            "Chiffres de référence (NE PAS modifier) :\n" +
            $"- Vitesse de croisière avant impact : {Math.Round(c.KphBef)} km/h\n" +
            $"- Vitesse maximale après impact : {Math.Round(c.KphAft)} km/h\n" +
            $"- Magnitude du choc (somme vectorielle MEMS, saturation ~222) : {Math.Round(c.Mag)}\n" +
            $"- Axes au pic : |X|={c.Ax} |Y|={c.Ay} |Z|={c.Az}\n" +
            $"- Événements haute-G comparables sur 7 jours : {c.NPrior7d}\n\n" +
            "Trames (t = secondes relatives à l'impact ; v = vitesse ; " +
            "mems = X/Y/Z accéléromètre borné ±127 ; cap = cap/heading ; ign = contact) :\n" +
            sb +
            "\nRédige la synthèse et les motifs en respectant strictement les règles et le format JSON.";
    }

    private static int Clamp(int? v) => v is null ? 0 : Math.Max(-128, Math.Min(127, v.Value));

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
