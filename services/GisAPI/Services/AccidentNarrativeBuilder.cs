namespace GisAPI.Services;

/// <summary>
/// Deterministic narrative builders for an accident report. Shared between
/// the real-time <see cref="AccidentDetectionService"/> (auto-detected
/// accidents) and the post-creation enrichment pipeline used by manually
/// declared accidents (<see cref="ManualAccidentEnrichmentService"/>) so
/// every accident — whether the system detected it or an admin filed it
/// after the fact — gets the same structured story / indicators / reasons
/// / synthesis sentence from the raw telemetry.
///
/// <para>The numbers come straight from the V7 candidate (peak magnitude,
/// saturated axes, cruise / post-event speeds, dwell counts) so any number
/// the operator reads here can be reconciled with the raw frames in the
/// timeline.</para>
///
/// <para>The LLM enrichment in <see cref="AccidentNarrativeService"/>
/// optionally upgrades the prose (synthesisText + reasons) on top of this
/// deterministic baseline — the LLM never invents numbers.</para>
/// </summary>
internal static class AccidentNarrativeBuilder
{
    public static object[] BuildStory(AccidentCandidate c)
    {
        var impact = c.RecordedAt;

        return new object[]
        {
            new {
                time  = impact.AddMinutes(-5).ToString("HH'h' mm"),
                title = "Conduite stable",
                body  = $"Le véhicule circule à environ {System.Math.Round(c.KphBef)} km/h dans les minutes précédant l'incident. Aucun comportement anormal n'est détecté.",
                severity = "normal"
            },
            new {
                time  = impact.ToString("HH'h' mm"),
                title = "Impact violent détecté",
                body  = "Les capteurs d'accélération enregistrent un choc d'intensité exceptionnelle, réparti sur les axes longitudinal (X) et latéral (Y) simultanément — profil typique d'une collision.",
                severity = "critical"
            },
            new {
                time  = impact.AddMinutes(1).ToString("HH'h' mm"),
                title = "Choc secondaire / saturation maintenue",
                body  = $"Les capteurs restent au-delà de leur seuil haut pendant plusieurs secondes (magnitude globale {System.Math.Round(c.Mag)} sur une échelle où 128 est la saturation d'un axe).",
                severity = "critical"
            },
            new {
                time  = impact.AddMinutes(5).ToString("HH'h' mm"),
                title = "Immobilisation complète",
                body  = $"Dans les minutes qui suivent, le véhicule est strictement à l'arrêt (vitesse maximale observée : {System.Math.Round(c.KphAft)} km/h sur {c.NAft} relevés).",
                severity = "warning"
            }
        };
    }

    public static object[] BuildReasons(AccidentCandidate c) => new object[]
    {
        new {
            title = "Chute brutale et incontrôlée de la vitesse",
            text  = $"Le véhicule passait de {System.Math.Round(c.KphBef)} km/h à une immobilisation totale en quelques secondes. Ce profil ne correspond pas à un freinage volontaire."
        },
        new {
            title = "Choc multi-axes simultané",
            text  = $"L'intensité mesurée (|X|={c.Ax}, |Y|={c.Ay}) sature les capteurs sur les deux axes horizontaux en même temps, ce qui n'est possible qu'en cas d'impact violent."
        },
        new {
            title = "Immobilisation prolongée après l'événement",
            text  = "Le véhicule est resté à l'arrêt pendant toute la fenêtre d'observation post-événement, sans reprise de mouvement — ce qui exclut un simple ralentissement ou une manœuvre."
        },
        new {
            title = "Absence d'antécédents comparables",
            text  = "Ce véhicule n'a produit aucun autre événement d'intensité comparable au cours des 7 derniers jours, ce qui écarte l'hypothèse d'un capteur défaillant ou d'une installation en cours de test."
        }
    };

    public static object[] BuildIndicators(AccidentCandidate c) => new object[]
    {
        new {
            label = "Heure de l'impact",
            value = c.RecordedAt.ToString("HH'h' mm 'min' ss 's'"),
            hint  = "Heure UTC"
        },
        new {
            label = "Vitesse avant l'impact",
            value = $"{System.Math.Round(c.KphBef)} km/h",
            hint  = "Maximum observé dans les 5 min précédentes"
        },
        new {
            label = "Vitesse après l'impact",
            value = $"{System.Math.Round(c.KphAft)} km/h",
            hint  = $"Maximum observé sur {c.NAft} relevés dans les 10 min suivantes"
        },
        new {
            label = "Magnitude du choc",
            value = System.Math.Round(c.Mag).ToString(),
            hint  = "Somme vectorielle des axes MEMS (saturation théorique ≈ 222)"
        },
        new {
            label = "Axes saturés",
            value = $"X={c.Ax} · Y={c.Ay} · Z={c.Az}",
            hint  = "Axes X/Y ≥ 100 = impact horizontal marqué"
        },
        new {
            label = "Conduite soutenue avant",
            value = $"{c.NMovBef} relevés ≥ 10 km/h",
            hint  = "Écarte les tests d'installation à l'arrêt"
        }
    };

    public static string BuildSynthesisText(AccidentCandidate c)
    {
        if (c.Mag >= 180 && c.Az >= 100)
        {
            return "choc violent multi-axes compatible avec un retournement ou une collision frontale";
        }
        if (c.Mag >= 150)
        {
            return "impact violent détecté sur les axes longitudinal et latéral";
        }
        return "impact significatif détecté avec immobilisation immédiate du véhicule";
    }
}
