using System.Globalization;

namespace GisAPI.Services;

/// <summary>
/// Deterministic narrative builders for an accident report. Shared between
/// the real-time <see cref="AccidentDetectionService"/> (auto-detected
/// accidents) and the post-creation enrichment used by manually declared
/// accidents (<see cref="ManualAccidentEnrichmentService"/>) so every
/// accident gets the same structured story / indicators / reasons /
/// synthesis from the raw telemetry.
///
/// <para><b>Client-facing vocabulary:</b> the original implementation used
/// technical accelerometer jargon (MEMS, axes X/Y/Z, magnitude /222,
/// "saturé à 127") that insurance experts and fleet managers couldn't
/// parse. The current version translates the raw signal into the
/// vocabulary a non-engineer expects to see on an insurance report:
/// "Choc frontal", "Choc latéral", "Très violent", "Retournement probable".
/// The numeric figures (speeds, durations) are kept because clients read
/// km/h and minutes; the internal indices (axis triplets, /222 scale)
/// never reach the output.</para>
///
/// <para><b>Adaptive output:</b> the story, the reasons list, the
/// indicators and the synthesis verdict are all chosen from the accident
/// PROFILE — direction (frontal / latéral / multi-direction / inconnu),
/// intensity (très violent / violent / modéré / faible), presence of a
/// sustained abnormal tilt (rollover), presence of a second shock, and
/// whether a tow truck pickup followed. Two accidents with different
/// profiles never produce the same prose.</para>
///
/// <para>The LLM enrichment in <see cref="AccidentNarrativeService"/>
/// optionally polishes the synthesis + reasons on top of this baseline
/// using the same vocabulary contract.</para>
/// </summary>
internal static class AccidentNarrativeBuilder
{
    // ─────────────────────────────────────────────────────────────────────
    // Profile detection — translates raw axis / magnitude / tilt numbers
    // into the qualitative categories the report exposes.
    // ─────────────────────────────────────────────────────────────────────

    public enum ImpactDirection
    {
        Unknown,
        Frontal,
        Lateral,
        FrontalLateral,
        Vertical,
        MultiDirection,
    }

    public enum ImpactIntensity
    {
        Negligible,
        Faible,
        Modere,
        Violent,
        TresViolent,
    }

    public static ImpactDirection DetectDirection(AccidentCandidate c)
    {
        // No MEMS evidence at all — the report must NOT claim a direction.
        if (c.Ax == 0 && c.Ay == 0 && c.Az == 0 && c.Mag < 1) return ImpactDirection.Unknown;

        // Sustained tilt after impact is the rollover fingerprint, regardless
        // of which horizontal axis took the hit.
        if (c.TiltDurationMin >= 2) return ImpactDirection.MultiDirection;

        var xHigh = c.Ax >= 100;
        var yHigh = c.Ay >= 100;
        var zHigh = c.Az >= 100;

        if (xHigh && yHigh) return ImpactDirection.FrontalLateral;
        if (xHigh) return ImpactDirection.Frontal;
        if (yHigh) return ImpactDirection.Lateral;
        if (zHigh) return ImpactDirection.Vertical;

        // Some shock detected but no axis dominates — generic multi-direction.
        if (c.Mag >= 80) return ImpactDirection.MultiDirection;
        return ImpactDirection.Unknown;
    }

    public static ImpactIntensity DetectIntensity(AccidentCandidate c)
    {
        if (c.Mag >= 180) return ImpactIntensity.TresViolent;
        if (c.Mag >= 130) return ImpactIntensity.Violent;
        if (c.Mag >= 80) return ImpactIntensity.Modere;
        if (c.Mag >= 40) return ImpactIntensity.Faible;
        return ImpactIntensity.Negligible;
    }

    public static string? DirectionLabel(ImpactDirection d) => d switch
    {
        ImpactDirection.Frontal => "Frontal",
        ImpactDirection.Lateral => "Latéral",
        ImpactDirection.FrontalLateral => "Frontal et latéral",
        ImpactDirection.Vertical => "Vertical",
        ImpactDirection.MultiDirection => "Multi-direction",
        _ => null,
    };

    public static string DirectionHint(ImpactDirection d, int tiltMin) => d switch
    {
        ImpactDirection.Frontal => "Choc subi à l'avant du véhicule",
        ImpactDirection.Lateral => "Choc subi sur le côté du véhicule",
        ImpactDirection.FrontalLateral => "Choc subi simultanément à l'avant et sur le côté",
        ImpactDirection.Vertical => "Choc à composante verticale dominante",
        ImpactDirection.MultiDirection when tiltMin >= 2 =>
            "Choc dans plusieurs directions, retournement probable",
        ImpactDirection.MultiDirection => "Choc dans plusieurs directions simultanément",
        _ => "Direction du choc non déterminée",
    };

    public static string IntensityLabel(ImpactIntensity i) => i switch
    {
        ImpactIntensity.TresViolent => "Très violent",
        ImpactIntensity.Violent => "Violent",
        ImpactIntensity.Modere => "Modéré",
        ImpactIntensity.Faible => "Faible",
        _ => "Négligeable",
    };

    public static string IntensityHint(ImpactIntensity i) => i switch
    {
        ImpactIntensity.TresViolent => "Intensité exceptionnelle — au-delà de toute conduite normale",
        ImpactIntensity.Violent => "Intensité incompatible avec un freinage volontaire",
        ImpactIntensity.Modere => "Intensité supérieure à un nid-de-poule ou un dos d'âne",
        ImpactIntensity.Faible => "Intensité faible mais détectable par les capteurs",
        _ => "Pas d'intensité significative mesurée",
    };

    // ─────────────────────────────────────────────────────────────────────
    // Story — adaptive timeline phases. We add phases only when the
    // underlying signal actually justifies them.
    // ─────────────────────────────────────────────────────────────────────

    public static object[] BuildStory(AccidentCandidate c)
    {
        var dir = DetectDirection(c);
        var intensity = DetectIntensity(c);
        var impact = c.RecordedAt;
        var phases = new List<object>();

        // Phase 1 — cruise. Always present, body adapts to whether we
        // actually have a "before" speed sample.
        var cruiseSpeed = Math.Round(c.KphBef);
        var cruiseBody = c.KphBef >= 5
            ? $"Le véhicule circule à environ {cruiseSpeed} km/h dans les minutes précédant l'incident. Aucun comportement anormal n'est détecté."
            : "Le véhicule est dans une phase de conduite normale dans les minutes précédant l'incident.";
        phases.Add(new
        {
            time = impact.AddMinutes(-5).ToString("HH'h'mm", CultureInfo.InvariantCulture),
            title = "Conduite normale",
            body = cruiseBody,
            severity = "normal",
        });

        // Phase 2 — impact. Title + body adapt to the detected direction.
        phases.Add(new
        {
            time = impact.ToString("HH'h'mm", CultureInfo.InvariantCulture),
            title = ImpactPhaseTitle(dir, intensity),
            body = ImpactPhaseBody(dir, intensity, c),
            severity = intensity >= ImpactIntensity.Violent ? "critical" : "warning",
        });

        // Phase 3 — second shock. Only when a second peak ≥ 100 was
        // detected at least 5 s from the primary.
        if (c.SecondShockMag >= 100)
        {
            phases.Add(new
            {
                time = impact.AddSeconds(30).ToString("HH'h'mm", CultureInfo.InvariantCulture),
                title = "Second choc",
                body = "Un second impact est enregistré dans la seconde fenêtre suivant le choc principal. Ce profil est typique d'un rebond contre un obstacle ou d'un mouvement de tonneau.",
                severity = "critical",
            });
        }

        // Phase 4 — sustained abnormal tilt = rollover. Only when |Z| stayed
        // ≥ 90 for at least 2 min after the vehicle stopped.
        if (c.TiltDurationMin >= 2)
        {
            phases.Add(new
            {
                time = impact.AddMinutes(1).ToString("HH'h'mm", CultureInfo.InvariantCulture),
                title = "Position anormale du véhicule",
                body = $"Le véhicule est resté immobilisé dans une position fortement inclinée pendant environ {c.TiltDurationMin} min après l'arrêt. Cette posture maintenue n'est pas compatible avec un stationnement et indique un retournement ou un basculement sur le flanc.",
                severity = "critical",
            });
        }

        // Phase 5 — full stop. Only when the post-event data really shows
        // immobilisation (KphAft ≤ 5).
        if (c.KphAft <= 5 && c.NAft >= 3)
        {
            phases.Add(new
            {
                time = impact.AddMinutes(5).ToString("HH'h'mm", CultureInfo.InvariantCulture),
                title = "Immobilisation complète",
                body = $"Dans les minutes qui suivent, le véhicule reste à l'arrêt. Vitesse maximale observée sur la période d'observation : {Math.Round(c.KphAft)} km/h.",
                severity = "warning",
            });
        }

        // Phase 6 — tow-truck pickup. Only when the tow detection module
        // has already stamped a TowEvent on this accident.
        if (c.HasTow)
        {
            phases.Add(new
            {
                time = impact.AddMinutes(30).ToString("HH'h'mm", CultureInfo.InvariantCulture),
                title = "Prise en charge par dépanneuse",
                body = "Un mouvement compatible avec un chargement sur dépanneuse est détecté après l'immobilisation. Le véhicule ne se déplace plus par ses propres moyens.",
                severity = "neutral",
            });
        }

        return phases.ToArray();
    }

    private static string ImpactPhaseTitle(ImpactDirection dir, ImpactIntensity intensity) => (dir, intensity) switch
    {
        (ImpactDirection.MultiDirection, ImpactIntensity.TresViolent) => "Choc d'une violence exceptionnelle",
        (ImpactDirection.FrontalLateral, _) => "Choc frontal et latéral",
        (ImpactDirection.Frontal, _) => "Collision frontale",
        (ImpactDirection.Lateral, _) => "Impact latéral",
        (ImpactDirection.Vertical, _) => "Impact vertical",
        (ImpactDirection.MultiDirection, _) => "Choc dans plusieurs directions",
        (ImpactDirection.Unknown, _) => "Événement détecté",
        _ => "Choc détecté",
    };

    private static string ImpactPhaseBody(ImpactDirection dir, ImpactIntensity intensity, AccidentCandidate c)
    {
        var beforeAfter = c.KphBef >= 10
            ? $"Vitesse passée de {Math.Round(c.KphBef)} km/h à {Math.Round(c.KphAft)} km/h en quelques secondes. "
            : "";

        return dir switch
        {
            ImpactDirection.Frontal =>
                $"{beforeAfter}Le choc est dirigé vers l'avant du véhicule, profil typique d'une collision frontale ou d'un heurt contre un obstacle fixe.",
            ImpactDirection.Lateral =>
                $"{beforeAfter}Le choc est dirigé sur le côté du véhicule, profil typique d'une collision en angle ou d'un impact reçu par le flanc.",
            ImpactDirection.FrontalLateral =>
                $"{beforeAfter}Le choc est subi simultanément à l'avant et sur le côté du véhicule, profil compatible avec une collision en angle violente ou un tonneau partiel.",
            ImpactDirection.Vertical =>
                $"{beforeAfter}Le choc présente une forte composante verticale, profil compatible avec une chute, un saut prononcé ou le début d'un retournement.",
            ImpactDirection.MultiDirection when c.TiltDurationMin >= 2 =>
                $"{beforeAfter}Le choc est suivi d'une position fortement inclinée maintenue — profil compatible avec un retournement ou un basculement du véhicule.",
            ImpactDirection.MultiDirection =>
                $"{beforeAfter}Le choc est mesuré dans plusieurs directions simultanément, profil incompatible avec un freinage ou une manœuvre ordinaire.",
            _ =>
                $"{beforeAfter}Un événement compatible avec un accident est détecté à cet instant.",
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reasons — conditional list of corroborating observations. We aim
    // for 3–4 reasons; only the ones whose criterion is actually true
    // for THIS accident are emitted.
    // ─────────────────────────────────────────────────────────────────────

    public static object[] BuildReasons(AccidentCandidate c)
    {
        var dir = DetectDirection(c);
        var intensity = DetectIntensity(c);
        var reasons = new List<object>();

        if (c.KphBef >= 30 && c.KphAft <= 5)
        {
            reasons.Add(new
            {
                title = "Chute brutale de la vitesse",
                text = $"Le véhicule est passé de {Math.Round(c.KphBef)} km/h à une immobilisation totale en quelques secondes. Ce profil ne correspond pas à un freinage volontaire.",
            });
        }

        if (intensity >= ImpactIntensity.Violent &&
            (dir == ImpactDirection.FrontalLateral || dir == ImpactDirection.MultiDirection))
        {
            reasons.Add(new
            {
                title = "Impact violent dans plusieurs directions",
                text = "L'intensité du choc a été enregistrée simultanément dans plusieurs directions, ce qui n'est possible qu'en cas de collision violente.",
            });
        }
        else if (intensity >= ImpactIntensity.Violent && dir == ImpactDirection.Frontal)
        {
            reasons.Add(new
            {
                title = "Choc frontal violent",
                text = "L'intensité enregistrée vers l'avant du véhicule est caractéristique d'une collision frontale.",
            });
        }
        else if (intensity >= ImpactIntensity.Violent && dir == ImpactDirection.Lateral)
        {
            reasons.Add(new
            {
                title = "Choc latéral violent",
                text = "L'intensité enregistrée sur le côté du véhicule est caractéristique d'un impact reçu par le flanc.",
            });
        }

        if (c.TiltDurationMin >= 2)
        {
            reasons.Add(new
            {
                title = "Position anormale maintenue après l'arrêt",
                text = $"Le véhicule est resté fortement incliné pendant environ {c.TiltDurationMin} min après son immobilisation. Cette posture maintenue est incompatible avec un stationnement régulier — elle indique un retournement ou un basculement.",
            });
        }

        if (c.NAft >= 6 && c.KphAft <= 1)
        {
            reasons.Add(new
            {
                title = "Immobilisation prolongée sans reprise de mouvement",
                text = "Le véhicule est resté strictement à l'arrêt pendant toute la fenêtre d'observation post-événement, ce qui exclut un simple ralentissement ou une manœuvre.",
            });
        }

        if (c.SecondShockMag >= 100)
        {
            reasons.Add(new
            {
                title = "Second choc détecté",
                text = "Un second impact distinct a été mesuré dans les secondes qui ont suivi le choc principal — profil typique d'un rebond ou d'un mouvement de tonneau.",
            });
        }

        if (c.NPrior7d == 0 && c.Mag >= 80)
        {
            reasons.Add(new
            {
                title = "Aucun antécédent comparable",
                text = "Ce véhicule n'a produit aucun autre événement d'intensité comparable au cours des 7 derniers jours, ce qui écarte l'hypothèse d'un capteur défaillant ou d'une fausse alerte récurrente.",
            });
        }

        if (c.HasTow)
        {
            reasons.Add(new
            {
                title = "Prise en charge par dépanneuse",
                text = "Le véhicule a été déplacé après l'événement par un mouvement compatible avec un chargement sur dépanneuse, et non par ses propres moyens.",
            });
        }

        // Fallback when nothing fired (typical for a manual report with no
        // MEMS evidence and no post-event stop signal): keep a single
        // honest line so the section is not empty.
        if (reasons.Count == 0)
        {
            reasons.Add(new
            {
                title = "Événement consigné par l'opérateur",
                text = "Cet accident a été déclaré manuellement. Les capteurs embarqués n'ont pas remonté de signature exploitable sur la fenêtre observée — l'analyse repose sur les éléments fournis par l'opérateur.",
            });
        }

        return reasons.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Indicators — compact KV table on top of the report. We only add a
    // row when the underlying value is meaningful for THIS accident.
    // ─────────────────────────────────────────────────────────────────────

    public static object[] BuildIndicators(AccidentCandidate c)
    {
        var dir = DetectDirection(c);
        var intensity = DetectIntensity(c);
        var rows = new List<object>();

        rows.Add(new
        {
            label = "Heure de l'impact",
            value = c.RecordedAt.ToString("HH'h'mm 'min' ss 's'", CultureInfo.InvariantCulture),
            hint = "Heure UTC",
        });

        if (c.Mag >= 1)
        {
            rows.Add(new
            {
                label = "Intensité du choc",
                value = IntensityLabel(intensity),
                hint = IntensityHint(intensity),
            });
        }

        var dirLabel = DirectionLabel(dir);
        if (dirLabel != null)
        {
            rows.Add(new
            {
                label = "Direction du choc",
                value = dirLabel,
                hint = DirectionHint(dir, c.TiltDurationMin),
            });
        }

        if (c.KphBef >= 1)
        {
            rows.Add(new
            {
                label = "Vitesse avant l'impact",
                value = $"{Math.Round(c.KphBef)} km/h",
                hint = "Vitesse maximale observée dans les 5 min précédentes",
            });
        }

        rows.Add(new
        {
            label = "Vitesse après l'impact",
            value = $"{Math.Round(c.KphAft)} km/h",
            hint = $"Vitesse maximale observée sur {c.NAft} relevés dans les 10 min suivantes",
        });

        if (c.TiltDurationMin >= 2)
        {
            rows.Add(new
            {
                label = "Position anormale maintenue",
                value = $"{c.TiltDurationMin} min",
                hint = "Durée pendant laquelle le véhicule est resté fortement incliné après l'arrêt",
            });
        }

        if (c.SecondShockMag >= 100)
        {
            rows.Add(new
            {
                label = "Second choc",
                value = "Détecté",
                hint = "Un second impact distinct est enregistré dans les secondes suivant le choc principal",
            });
        }

        if (c.NMovBef >= 1)
        {
            rows.Add(new
            {
                label = "Conduite soutenue avant",
                value = $"{c.NMovBef} relevés ≥ 10 km/h",
                hint = "Le véhicule était bien en mouvement avant l'événement — écarte les tests à l'arrêt",
            });
        }

        return rows.ToArray();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Synthesis — one-paragraph headline shown at the top of the report.
    // Combines direction + intensity + key numbers to produce a unique
    // sentence per accident profile.
    // ─────────────────────────────────────────────────────────────────────

    public static string BuildSynthesisText(AccidentCandidate c)
    {
        var dir = DetectDirection(c);
        var intensity = DetectIntensity(c);
        var hhmm = c.RecordedAt.ToString("HH'h'mm", CultureInfo.InvariantCulture);
        var speedDrop = c.KphBef >= 10
            ? $"Vitesse passée de {Math.Round(c.KphBef)} à {Math.Round(c.KphAft)} km/h en moins de 30 secondes. "
            : "";

        // Rollover scenario — tilt sustained for at least 2 min after stop.
        if (c.TiltDurationMin >= 2)
        {
            return $"Choc d'une intensité {IntensityWord(intensity)} à {hhmm}. {speedDrop}" +
                   $"Position anormalement inclinée maintenue {c.TiltDurationMin} min après l'arrêt — retournement très probable.";
        }

        // Multi-direction violent shock without rollover.
        if (dir == ImpactDirection.FrontalLateral && intensity >= ImpactIntensity.Violent)
        {
            return $"Choc {IntensityWord(intensity)} frontal et latéral simultané à {hhmm}. " +
                   $"{speedDrop}Profil compatible avec une collision en angle.";
        }

        // Pure frontal collision.
        if (dir == ImpactDirection.Frontal && intensity >= ImpactIntensity.Violent)
        {
            return $"Collision frontale {IntensityWord(intensity)} à {hhmm}. " +
                   $"{speedDrop}Le véhicule a subi un impact vers l'avant sans freinage progressif.";
        }

        // Pure lateral impact.
        if (dir == ImpactDirection.Lateral && intensity >= ImpactIntensity.Violent)
        {
            return $"Impact latéral {IntensityWord(intensity)} à {hhmm}. " +
                   $"{speedDrop}Le véhicule a reçu le choc sur son flanc.";
        }

        // Moderate impact.
        if (intensity == ImpactIntensity.Modere)
        {
            return $"Impact modéré détecté à {hhmm}. {speedDrop}" +
                   "Le véhicule s'est ensuite immobilisé sur la zone.";
        }

        // No MEMS evidence — manual declaration with no sensor signal.
        if (intensity == ImpactIntensity.Negligible || dir == ImpactDirection.Unknown)
        {
            return $"Événement déclaré à {hhmm}. Aucun signal de choc exploitable n'a été remonté par les capteurs sur la fenêtre observée — l'analyse repose sur la trajectoire GPS et les éléments fournis par l'opérateur.";
        }

        // Fallback — weak / generic shock.
        return $"Impact significatif détecté à {hhmm}. {speedDrop}Le véhicule s'est immobilisé immédiatement.";
    }

    private static string IntensityWord(ImpactIntensity i) => i switch
    {
        ImpactIntensity.TresViolent => "exceptionnelle",
        ImpactIntensity.Violent => "violente",
        ImpactIntensity.Modere => "modérée",
        ImpactIntensity.Faible => "faible",
        _ => "limitée",
    };
}
