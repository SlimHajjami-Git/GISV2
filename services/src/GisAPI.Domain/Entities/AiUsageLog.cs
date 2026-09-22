namespace GisAPI.Domain.Entities;

/// <summary>
/// Un appel payant à l'IA (Groq) réussi, AUTRE qu'un scan de facture : assistant IA,
/// comparaison et rapport véhicule, rapport IA flotte et ses questions, explication d'une
/// tranche de consommation, récit d'accident (migration 052, table ai_usage_logs).
///
/// <para>Pourquoi un journal : le crédit IA mensuel de la société (22/09/2026, demande de
/// Slim « le quota inclut l'utilisation de l'IA ») couvre TOUTE l'IA, pas seulement le scan.
/// La consommation du mois = jetons des scans (table à part, <see cref="InvoiceScanLog"/>,
/// jamais recopiés ici : pas de double comptage) + somme de ces lignes depuis le 1er.</para>
/// </summary>
public class AiUsageLog
{
    public long Id { get; set; }

    public int CompanyId { get; set; }

    /// <summary>Utilisateur à l'origine de l'appel ; null pour un appel déclenché par le
    /// système (récit d'accident rédigé à la détection).</summary>
    public int? UserId { get; set; }

    /// <summary>Fonction appelante (assistant_chat, fleet_report…) : ventilation du mois
    /// sur la fiche société de l'admin.</summary>
    public string Feature { get; set; } = string.Empty;

    /// <summary>Jetons Groq consommés par l'appel (0 = fournisseur muet : le crédit compte
    /// alors une estimation par fonction, jamais zéro).</summary>
    public int TokensUsed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
