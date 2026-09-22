using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Costs;

/// <summary>
/// État du crédit IA du mois pour une société — ce que reçoivent l'écran client (barre
/// « Crédit IA » à côté du bouton « Scanner une facture ») et la fiche société de l'admin.
///
/// <para>Invariant voulu : <c>PercentUsed == 100</c> si et seulement si
/// <c>RemainingTokens == 0</c>. Le pourcentage est arrondi VERS LE BAS : un arrondi au plus
/// proche afficherait « 100 % » (bouton grisé) à 59 999 / 60 000 jetons, alors que le
/// serveur accepte encore un scan.</para>
/// </summary>
public sealed record InvoiceScanCreditStatus(
    bool Enabled,
    int BudgetTokens,
    int UsedTokens,
    int RemainingTokens,
    int PercentUsed,
    int ScansThisMonth,
    int EstimatedScansLeft,
    DateTime ResetsAt);

/// <summary>
/// Crédit IA MENSUEL du scan de factures, en jetons Groq (demande de Slim du 22/09/2026 :
/// « une barre de progression de contexte qui se remet à zéro chaque mois »).
///
/// <para>Pourquoi des jetons et plus un nombre de scans : un scan ne coûte pas toujours la
/// même chose. Mesuré sur TN (13 scans) : de 1 953 à 3 957 jetons, 3 000 en moyenne — une
/// photo floue déclenche une passe corrective qui double la note. Le compteur « 12/20 ce
/// mois » ne disait rien de ce que la société consomme réellement.</para>
///
/// <para>Une seule source pour le budget, la consommation et la date de recharge : le
/// contrôle avant scan (CostsController), le compteur de l'écran et la fiche admin
/// (GetSocieteById) passent tous par ici, pour ne jamais afficher un chiffre que le serveur
/// n'applique pas.</para>
/// </summary>
public static class InvoiceScanCredit
{
    /// <summary>Crédit par défaut d'une société sans réglage propre : ≈ 20 scans, le même
    /// ordre de grandeur que l'ancien quota par défaut de 20 scans.</summary>
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
    /// <item><paramref name="monthlyTokens"/> renseigné : il fait foi (0 = désactivé).</item>
    /// <item>Sinon l'ANCIEN quota en scans, s'il existe, converti à 3 000 jetons par scan :
    /// une société réglée à 50 scans garde ≈ 50 scans, et 0 reste 0 (désactivé). Relire
    /// ce nombre tel quel comme des jetons l'aurait privée de tout scan. La conversion est
    /// BORNÉE au plafond du réglage (<see cref="MaxMonthlyTokens"/>) : l'ancien écran
    /// acceptait jusqu'à 100 000 scans, soit 300 millions de jetons, et la fiche préremplit
    /// son champ avec ce budget — sans borne, un « Enregistrer » sans retouche se faisait
    /// refuser (« entre 0 et 10 000 000 jetons ») pour un réglage que l'admin n'a pas touché.</item>
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
    /// « 20 scans par défaut ». Effacer cette colonne à chaque réglage faisait qu'un
    /// <c>kubectl rollout undo</c> rouvrait le scan à une société coupée (0 jeton) — ce
    /// quota est le SEUL contrôle d'accès de la fonction (décision du 19/09/2026) — et
    /// ramenait à 20 scans une société réglée plus haut.</para>
    /// <list type="bullet">
    /// <item>null (retour au défaut) → NULL : le défaut, des deux côtés, et l'ancien réglage
    /// n'est plus relu (sinon « vide = défaut » rendrait 50 scans × 3 000 jetons).</item>
    /// <item>0 (désactivé) → 0 : un retour arrière garde la fonction fermée.</item>
    /// <item>sinon → le nombre de scans moyens équivalent, au moins 1 (un crédit positif ne
    /// devient jamais « désactivé » dans l'ancien code).</item>
    /// </list>
    /// </summary>
    public static int? LegacyScanLimitShadow(int? monthlyTokens) => monthlyTokens switch
    {
        null => null,
        <= 0 => 0,
        int tokens => Math.Max(1, tokens / EstimatedTokensPerScan)
    };

    /// <summary>
    /// Jetons décomptés pour UN scan. Groq rend normalement sa consommation (1 953 à 3 957
    /// en prod), mais le client HTTP retombe sur 0 quand la réponse n'a pas de bloc
    /// « usage ». Compter ce scan pour 0 le rendrait gratuit : avec l'ancien quota chaque
    /// scan comptait, un modèle de repli muet sur sa consommation aurait offert un crédit
    /// illimité. Un scan à consommation inconnue compte donc pour le coût moyen.
    /// <para>La requête de <see cref="MonthlyConsumptionAsync"/> applique la MÊME règle en
    /// SQL (une méthode C# n'y est pas traduisible) : les deux sont vérifiées ensemble.</para>
    /// </summary>
    public static int ChargedTokens(int tokensUsed) =>
        tokensUsed > 0 ? tokensUsed : EstimatedTokensPerScan;

    /// <summary>Début du mois civil en cours (UTC) : tout scan journalisé depuis compte.</summary>
    public static DateTime MonthStart(DateTime utcNow) =>
        new(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Prochaine recharge du crédit : le 1er du mois suivant, minuit UTC.</summary>
    public static DateTime NextReset(DateTime utcNow) => MonthStart(utcNow).AddMonths(1);

    /// <summary>Assemble l'état affiché à partir du budget et de la consommation.</summary>
    public static InvoiceScanCreditStatus Compute(int budgetTokens, int usedTokens, int scansThisMonth, DateTime resetsAt)
    {
        var budget = Math.Max(0, budgetTokens);
        var used = Math.Max(0, usedTokens);
        var remaining = Math.Max(0, budget - used);

        // Budget nul (fonction désactivée, société introuvable) : rien n'est disponible,
        // donc 100 % — c'est Enabled qui dit à l'écran qu'il s'agit d'une fermeture et non
        // d'un crédit épuisé.
        var percent = budget == 0
            ? 100
            : (int)Math.Min(100, (long)used * 100 / budget);

        return new InvoiceScanCreditStatus(
            Enabled: budget > 0,
            BudgetTokens: budget,
            UsedTokens: used,
            RemainingTokens: remaining,
            PercentUsed: percent,
            ScansThisMonth: Math.Max(0, scansThisMonth),
            EstimatedScansLeft: remaining / EstimatedTokensPerScan,
            ResetsAt: resetsAt);
    }

    /// <summary>
    /// Consommation du mois de la société : somme des jetons des scans réussis journalisés
    /// dans invoice_scan_logs depuis le 1er (index company_id, created_at), et leur nombre.
    /// Agrégée en SQL : rien n'est matérialisé côté C#.
    /// </summary>
    public static async Task<(int UsedTokens, int Scans)> MonthlyConsumptionAsync(
        IGisDbContext context, int companyId, DateTime monthStart, CancellationToken ct)
    {
        var logs = context.InvoiceScanLogs
            .AsNoTracking()
            .Where(l => l.CompanyId == companyId && l.CreatedAt >= monthStart);

        var scans = await logs.CountAsync(ct);
        if (scans == 0)
            return (0, 0);

        // Même règle que ChargedTokens, écrite ici pour être traduite en SQL.
        var used = await logs.SumAsync(l => l.TokensUsed > 0 ? l.TokensUsed : EstimatedTokensPerScan, ct);
        return (used, scans);
    }

    /// <summary>
    /// Crédit du mois en cours pour une société. Société introuvable (jeton sans
    /// revendication companyId, société supprimée) : crédit NUL, jamais le défaut de la
    /// plateforme — ce crédit porte SEUL le droit d'user du scan (décision du 19/09/2026),
    /// et un jeton hors société ne doit rien pouvoir consommer.
    /// </summary>
    public static async Task<InvoiceScanCreditStatus> LoadAsync(
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
        var (used, scans) = await MonthlyConsumptionAsync(context, companyId, monthStart, ct);
        return Compute(budget, used, scans, resetsAt);
    }
}
