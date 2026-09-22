using System.Text.Json.Serialization;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GisAPI.Application.Features.AiCredits;

/// <summary>
/// Fonctions qui consomment le crédit IA de la société, telles qu'elles sont journalisées
/// (colonne ai_usage_logs.feature, clés de la ventilation « byFeature »).
/// </summary>
public static class AiFeatures
{
    /// <summary>Scan de facture — journalisé à part, dans invoice_scan_logs (historique).</summary>
    public const string InvoiceScan = "invoice_scan";
    /// <summary>Assistant IA d'un véhicule (POST /api/ai-chat/send).</summary>
    public const string AssistantChat = "assistant_chat";
    /// <summary>Comparaison de véhicules (POST /api/ai-chat/compare).</summary>
    public const string VehicleCompare = "vehicle_compare";
    /// <summary>Rapport diagnostic d'un véhicule (GET /api/ai-chat/report/{id}).</summary>
    public const string VehicleReport = "vehicle_report";
    /// <summary>Rapport IA flotte (POST /api/ai-chat/fleet-report).</summary>
    public const string FleetReport = "fleet_report";
    /// <summary>Question sur le rapport IA flotte (POST /api/ai-chat/fleet-report/ask).</summary>
    public const string FleetReportAsk = "fleet_report_ask";
    /// <summary>Explication d'une tranche de consommation (POST /api/consumption-analysis/explain-segment).</summary>
    public const string ConsumptionExplain = "consumption_explain";
    /// <summary>Récit d'accident rédigé par l'IA à la détection ou à la déclaration (jamais bloqué).</summary>
    public const string AccidentNarrative = "accident_narrative";

    /// <summary>Toutes les fonctions, dans l'ordre de la ventilation (fiche admin).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        InvoiceScan, AssistantChat, VehicleCompare, VehicleReport,
        FleetReport, FleetReportAsk, ConsumptionExplain, AccidentNarrative
    };

    /// <summary>Estimation d'une fonction inconnue de cette liste.</summary>
    public const int DefaultEstimatedTokens = 2_500;

    /// <summary>
    /// Jetons comptés pour un appel dont Groq n'a pas rendu la consommation (bloc « usage »
    /// absent : le client HTTP retombe sur 0). Le compter pour 0 le rendrait GRATUIT, et un
    /// modèle de repli muet offrirait un crédit illimité. Estimation PRUDENTE (vers le haut)
    /// par fonction, d'après la taille du prompt et le plafond de réponse de chaque appel :
    /// <list type="bullet">
    /// <item>scan : 3 000, coût moyen mesuré sur TN (13 scans, 1 953 à 3 957) ;</item>
    /// <item>assistant : 2 500 (mesure TN : 2 400 en moyenne, 3 400 au plus) ;</item>
    /// <item>comparaison : 3 000 (jusqu'à 5 véhicules dans le prompt) ;</item>
    /// <item>rapport véhicule : 3 500 (contexte complet + rapport en 8 parties) ;</item>
    /// <item>rapport flotte : 5 000 (gros prompt + 2 500 de réponse) ;</item>
    /// <item>question sur le rapport : 3 500 (contexte tronqué + 2 000 de réponse) ;</item>
    /// <item>explication de tranche : 1 000 (dossier court + 500 de réponse) ;</item>
    /// <item>récit d'accident : 2 500 (relevés autour du choc + 900 de réponse).</item>
    /// </list>
    /// </summary>
    public static int EstimatedTokens(string? feature) => feature switch
    {
        InvoiceScan => AiCredit.EstimatedTokensPerScan,
        AssistantChat => 2_500,
        VehicleCompare => 3_000,
        VehicleReport => 3_500,
        FleetReport => 5_000,
        FleetReportAsk => 3_500,
        ConsumptionExplain => 1_000,
        AccidentNarrative => 2_500,
        _ => DefaultEstimatedTokens
    };
}

/// <summary>
/// Crédit IA du mois d'une société — le contrat JSON lu par la barre « Crédit IA » (scan de
/// factures, assistant IA, rapport IA flotte) et par la fiche société de l'admin. Les noms
/// JSON sont écrits ici plutôt que confiés à la politique de sérialisation : le même objet
/// part par MVC (camelCase), par ExceptionHandlingMiddleware (refus 403/429) et dans les
/// tests (sérialiseur par défaut).
///
/// <para>Invariant voulu : <c>PercentUsed == 100</c> si et seulement si
/// <c>RemainingTokens == 0</c>. Le pourcentage est arrondi VERS LE BAS : un arrondi au plus
/// proche afficherait « 100 % » (boutons grisés) à 59 999 / 60 000 jetons, alors que le
/// serveur accepte encore un appel.</para>
///
/// <para><c>ByFeature</c> : jetons du mois par fonction (toutes les clés de
/// <see cref="AiFeatures.All"/>, 0 compris) ; leur somme vaut <c>UsedTokens</c>.</para>
/// </summary>
public sealed record AiCreditStatus(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("budgetTokens")] int BudgetTokens,
    [property: JsonPropertyName("usedTokens")] int UsedTokens,
    [property: JsonPropertyName("remainingTokens")] int RemainingTokens,
    [property: JsonPropertyName("percentUsed")] int PercentUsed,
    [property: JsonPropertyName("scansThisMonth")] int ScansThisMonth,
    [property: JsonPropertyName("estimatedScansLeft")] int EstimatedScansLeft,
    [property: JsonPropertyName("resetsAt")] DateTime ResetsAt,
    [property: JsonPropertyName("byFeature")] IReadOnlyDictionary<string, int> ByFeature);

/// <summary>Consommation du mois d'une société : nombre de scans et jetons par fonction.</summary>
public sealed record AiMonthlyConsumption(int ScansThisMonth, IReadOnlyDictionary<string, int> ByFeature)
{
    /// <summary>Total du mois (somme de la ventilation, bornée à int.MaxValue).</summary>
    public int UsedTokens => AiCredit.Clamp(ByFeature.Values.Sum(v => (long)v));
}

/// <summary>
/// Crédit IA MENSUEL de la société, en jetons Groq — UNE seule source pour le budget, la
/// consommation, la date de recharge, le contrôle avant chaque appel payant et
/// l'enregistrement après chaque appel réussi.
///
/// <para>Historique : 22/09/2026, le quota en nombre de scans devient un crédit en jetons
/// (un scan coûte de 1 953 à 3 957 jetons, 3 000 en moyenne) ; le même jour, demande de
/// Slim : « et bien sûr le quota inclut l'utilisation de l'IA » — le crédit couvre TOUTE
/// l'IA de la société (scan, assistant, comparaison et rapport véhicule, rapport flotte et
/// ses questions, explication de consommation, récit d'accident), pas seulement le scan.
/// Seul l'assistant PUBLIC d'avant connexion (sans société) n'y est pas compté.</para>
///
/// <para>Consommation du mois = jetons des scans (invoice_scan_logs, 0 → 3 000) + jetons
/// des autres appels (ai_usage_logs, 0 → estimation par fonction), depuis le 1er (UTC).
/// Tous les appelants passent par ici — contrôle, barre des écrans, fiche admin — pour ne
/// jamais afficher un chiffre que le serveur n'applique pas.</para>
/// </summary>
public static class AiCredit
{
    /// <summary>Crédit par défaut d'une société sans réglage propre : ≈ 20 scans, ou
    /// ≈ 24 réponses de l'assistant, ou un mélange des deux.</summary>
    public const int DefaultMonthlyTokens = 60_000;

    /// <summary>Coût moyen mesuré d'un scan (TN, septembre 2026). Sert à convertir l'ancien
    /// quota en scans, à estimer les scans restants, et à décompter un scan dont Groq n'a
    /// pas rendu la consommation.</summary>
    public const int EstimatedTokensPerScan = 3_000;

    /// <summary>Plafond du réglage admin (≈ 3 300 scans par mois).</summary>
    public const int MaxMonthlyTokens = 10_000_000;

    /// <summary>
    /// Budget effectif du mois, en jetons.
    /// <list type="bullet">
    /// <item><paramref name="monthlyTokens"/> renseigné : il fait foi (0 = IA désactivée).</item>
    /// <item>Sinon l'ANCIEN quota en scans, s'il existe, converti à 3 000 jetons par scan :
    /// une société réglée à 50 scans garde ≈ 50 scans, et 0 reste 0. Relire ce nombre tel
    /// quel comme des jetons l'aurait privée de tout. La conversion est BORNÉE au plafond du
    /// réglage (<see cref="MaxMonthlyTokens"/>) : l'ancien écran acceptait jusqu'à
    /// 100 000 scans, et la fiche préremplit son champ avec ce budget — sans borne, un
    /// « Enregistrer » sans retouche se faisait refuser.</item>
    /// <item>Sinon le défaut plateforme.</item>
    /// </list>
    /// </summary>
    public static int EffectiveBudget(int? monthlyTokens, int? legacyMonthlyScanLimit)
    {
        if (monthlyTokens is int tokens)
            return Math.Max(0, tokens);

        if (legacyMonthlyScanLimit is int scans)
            return scans <= 0 ? 0 : (int)Math.Min((long)scans * EstimatedTokensPerScan, MaxMonthlyTokens);

        return DefaultMonthlyTokens;
    }

    /// <summary>
    /// Valeur écrite dans l'ANCIENNE colonne (quota en scans) quand l'admin règle le crédit
    /// en jetons : une OMBRE pour le retour arrière, jamais relue par ce code tant que les
    /// jetons sont renseignés.
    /// <para>Pourquoi : l'ancien pod ne lit QUE invoice_scan_monthly_limit, et NULL y vaut
    /// « 20 scans par défaut ». L'effacer à chaque réglage faisait qu'un
    /// <c>kubectl rollout undo</c> rouvrait le scan à une société coupée (0 jeton) et
    /// ramenait à 20 scans une société réglée plus haut.</para>
    /// <list type="bullet">
    /// <item>null (retour au défaut) → NULL : le défaut, des deux côtés.</item>
    /// <item>0 (désactivé) → 0 : un retour arrière garde la fonction fermée.</item>
    /// <item>sinon → le nombre de scans moyens équivalent, au moins 1.</item>
    /// </list>
    /// </summary>
    public static int? LegacyScanLimitShadow(int? monthlyTokens) => monthlyTokens switch
    {
        null => null,
        <= 0 => 0,
        int tokens => Math.Max(1, tokens / EstimatedTokensPerScan)
    };

    /// <summary>
    /// Jetons décomptés pour UN appel de la fonction <paramref name="feature"/> : ses jetons
    /// réels, ou l'estimation de la fonction quand Groq n'a rien rendu (0) — un appel à
    /// consommation inconnue n'est jamais gratuit (voir <see cref="AiFeatures.EstimatedTokens"/>).
    /// <para>La requête de <see cref="MonthlyConsumptionAsync"/> applique la MÊME règle en
    /// SQL (une méthode C# n'y est pas traduisible) : les deux sont vérifiées ensemble.</para>
    /// </summary>
    public static int ChargedTokens(string feature, int tokensUsed) =>
        tokensUsed > 0 ? tokensUsed : AiFeatures.EstimatedTokens(feature);

    /// <summary>Début du mois civil en cours (UTC) : tout appel journalisé depuis compte.</summary>
    public static DateTime MonthStart(DateTime utcNow) =>
        new(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Prochaine recharge du crédit : le 1er du mois suivant, minuit UTC.</summary>
    public static DateTime NextReset(DateTime utcNow) => MonthStart(utcNow).AddMonths(1);

    /// <summary>Ventilation vide : toutes les fonctions connues, à 0.</summary>
    public static IReadOnlyDictionary<string, int> EmptyBreakdown()
    {
        var byFeature = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in AiFeatures.All)
            byFeature[f] = 0;
        return byFeature;
    }

    internal static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

    /// <summary>
    /// Assemble l'état affiché à partir du budget et de la consommation.
    /// <paramref name="byFeature"/> absent : ventilation à 0 (tests des règles pures).
    /// </summary>
    public static AiCreditStatus Compute(int budgetTokens, int usedTokens, int scansThisMonth, DateTime resetsAt,
        IReadOnlyDictionary<string, int>? byFeature = null)
    {
        var budget = Math.Max(0, budgetTokens);
        var used = Math.Max(0, usedTokens);
        var remaining = Math.Max(0, budget - used);

        // Budget nul (IA désactivée, société introuvable) : rien n'est disponible, donc
        // 100 % — c'est Enabled qui dit à l'écran qu'il s'agit d'une fermeture et non d'un
        // crédit épuisé.
        var percent = budget == 0
            ? 100
            : (int)Math.Min(100, (long)used * 100 / budget);

        return new AiCreditStatus(
            Enabled: budget > 0,
            BudgetTokens: budget,
            UsedTokens: used,
            RemainingTokens: remaining,
            PercentUsed: percent,
            ScansThisMonth: Math.Max(0, scansThisMonth),
            EstimatedScansLeft: remaining / EstimatedTokensPerScan,
            ResetsAt: resetsAt,
            ByFeature: byFeature ?? EmptyBreakdown());
    }

    /// <summary>
    /// Consommation du mois de la société, agrégée en SQL (index company_id, created_at sur
    /// les deux journaux ; au plus une ligne par fonction remonte côté C#) :
    /// <list type="bullet">
    /// <item>scans réussis journalisés dans invoice_scan_logs (0 jeton → 3 000) ;</item>
    /// <item>autres appels journalisés dans ai_usage_logs, par fonction (0 jeton → estimation
    /// de la fonction).</item>
    /// </list>
    /// </summary>
    public static async Task<AiMonthlyConsumption> MonthlyConsumptionAsync(
        IGisDbContext context, int companyId, DateTime monthStart, CancellationToken ct)
    {
        var byFeature = new Dictionary<string, int>(EmptyBreakdown(), StringComparer.Ordinal);

        var scanLogs = context.InvoiceScanLogs
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.CreatedAt >= monthStart);
        var scans = await scanLogs.CountAsync(ct);
        if (scans > 0)
        {
            // Même règle que ChargedTokens(InvoiceScan, …), écrite ici pour être traduite en SQL.
            byFeature[AiFeatures.InvoiceScan] = await scanLogs
                .SumAsync(l => l.TokensUsed > 0 ? l.TokensUsed : EstimatedTokensPerScan, ct);
        }

        // Jetons connus et appels muets, par fonction : l'estimation d'un appel muet dépend de
        // la fonction, elle est donc appliquée ici au nombre d'appels muets remonté par SQL.
        var usages = await context.AiUsageLogs
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.CreatedAt >= monthStart)
            .GroupBy(l => l.Feature)
            .Select(g => new
            {
                Feature = g.Key,
                Known = g.Sum(l => l.TokensUsed > 0 ? l.TokensUsed : 0),
                Silent = g.Count(l => l.TokensUsed <= 0)
            })
            .ToListAsync(ct);

        foreach (var u in usages)
        {
            var total = (long)u.Known + (long)u.Silent * AiFeatures.EstimatedTokens(u.Feature);
            byFeature[u.Feature] = Clamp(byFeature.GetValueOrDefault(u.Feature) + total);
        }

        return new AiMonthlyConsumption(scans, byFeature);
    }

    /// <summary>
    /// Crédit du mois en cours pour une société. Société introuvable (jeton sans
    /// revendication companyId, société supprimée) : crédit NUL, jamais le défaut de la
    /// plateforme — ce crédit porte SEUL le droit d'user de l'IA (décision du 19/09/2026),
    /// et un jeton hors société ne doit rien pouvoir consommer.
    /// </summary>
    public static async Task<AiCreditStatus> LoadAsync(
        IGisDbContext context, int companyId, DateTime utcNow, CancellationToken ct)
    {
        var monthStart = MonthStart(utcNow);
        var resetsAt = monthStart.AddMonths(1);

        var societe = await context.Societes
            .AsNoTracking()
            .Where(s => s.Id == companyId)
            .Select(s => new { s.InvoiceScanMonthlyTokens, s.InvoiceScanMonthlyLimit })
            .FirstOrDefaultAsync(ct);
        if (societe is null)
            return Compute(0, 0, 0, resetsAt);

        var budget = EffectiveBudget(societe.InvoiceScanMonthlyTokens, societe.InvoiceScanMonthlyLimit);
        var consumption = await MonthlyConsumptionAsync(context, companyId, monthStart, ct);
        return Compute(budget, consumption.UsedTokens, consumption.ScansThisMonth, resetsAt, consumption.ByFeature);
    }

    /// <summary>Refus à opposer à un appel payant, ou null si le crédit le permet :
    /// IA désactivée (403) avant crédit épuisé (429).</summary>
    public static AiCreditException? Refusal(AiCreditStatus credit) =>
        !credit.Enabled ? AiCreditException.Disabled(credit)
        : credit.RemainingTokens <= 0 ? AiCreditException.Exhausted(credit)
        : null;

    /// <summary>
    /// Contrôle AVANT chaque appel payant déclenché par un utilisateur : lève
    /// <see cref="AiCreditException"/> (403 AI_CREDIT_DISABLED ou 429 AI_CREDIT_EXHAUSTED,
    /// traduite par ExceptionHandlingMiddleware) et rend sinon le crédit du moment.
    /// <para>Le dernier appel du mois peut dépasser le budget de quelques milliers de jetons
    /// (on ne connaît son coût qu'après) : la barre plafonne alors à 100 % et l'appel suivant
    /// est refusé.</para>
    /// </summary>
    public static async Task<AiCreditStatus> EnsureAvailableAsync(
        IGisDbContext context, int companyId, CancellationToken ct)
    {
        var credit = await LoadAsync(context, companyId, DateTime.UtcNow, ct);
        var refus = Refusal(credit);
        if (refus is not null)
            throw refus;
        return credit;
    }

    /// <summary>
    /// Enregistre UN appel réussi (à appeler APRÈS le retour de l'IA, jamais sur un échec) :
    /// un scan va dans invoice_scan_logs, tout le reste dans ai_usage_logs — une seule ligne
    /// par appel, dans une seule table, donc aucun double comptage. Sauvegarde aussitôt.
    /// Société inconnue (≤ 0) : rien n'est écrit — jamais de ligne sous la société 0.
    /// </summary>
    public static async Task LogUsageAsync(
        IGisDbContext context, int companyId, int? userId, string feature, int tokensUsed)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(feature))
            return;

        var tokens = Math.Max(0, tokensUsed);
        var now = DateTime.UtcNow;
        EntityEntry ligne;
        if (feature == AiFeatures.InvoiceScan)
        {
            ligne = context.InvoiceScanLogs.Add(new InvoiceScanLog
            {
                CompanyId = companyId,
                UserId = userId ?? 0,
                TokensUsed = tokens,
                CreatedAt = now
            });
        }
        else
        {
            ligne = context.AiUsageLogs.Add(new AiUsageLog
            {
                CompanyId = companyId,
                UserId = userId is > 0 ? userId : null,
                Feature = feature.Length > 32 ? feature[..32] : feature,
                TokensUsed = tokens,
                CreatedAt = now
            });
        }

        try
        {
            // Jamais annulé par la requête : l'appel est DÉJÀ payé à Groq. Un navigateur qui
            // abandonne la page pendant la réponse annulerait sinon l'écriture, et l'appel
            // deviendrait gratuit — un crédit contournable en fermant l'onglet au bon moment.
            // D'où l'absence de jeton d'annulation dans la signature.
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // Écriture refusée (table absente quand le pod précède la migration 052, base
            // coupée, disque plein) : la ligne sort du suivi AVANT que l'erreur remonte. Le
            // contexte est celui de l'APPELANT — le cycle de détection des accidents en partage
            // un seul entre tous ses candidats. Restée « à insérer », la ligne ferait échouer
            // chacune de ses sauvegardes suivantes : récit IA perdu, accident suivant ni créé
            // ni notifié. L'appelant décide ensuite de l'erreur (réponse en erreur côté API,
            // simple journal pour le récit d'accident, qui n'est jamais bloqué).
            ligne.State = EntityState.Detached;
            throw;
        }
    }

    /// <summary>
    /// Enregistre un appel réussi puis RELIT le crédit : la barre reçoit exactement ce que le
    /// prochain contrôle appliquera (appels simultanés compris), plutôt qu'un calcul à la main.
    /// </summary>
    public static async Task<AiCreditStatus> RecordUsageAsync(
        IGisDbContext context, int companyId, int? userId, string feature, int tokensUsed, CancellationToken ct)
    {
        await LogUsageAsync(context, companyId, userId, feature, tokensUsed);
        // Relecture seule sous le jeton de la requête : l'écriture, elle, est déjà faite.
        return await LoadAsync(context, companyId, DateTime.UtcNow, ct);
    }
}
