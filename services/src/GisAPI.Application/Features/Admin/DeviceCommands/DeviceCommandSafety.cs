namespace GisAPI.Application.Features.Admin.DeviceCommands;

/// <summary>Verdict du garde-fou : texte normalisé (retour à la ligne inclus) ou motif de refus.</summary>
public sealed record DeviceCommandCheck(bool Ok, string Normalized, string? Reason);

/// <summary>
/// Garde-fou de l'écran admin « Commandes boîtiers ». Tout ce qui part vers un
/// boîtier passe ici AVANT d'être écrit dans device_commands.
///
/// <para>Règle non négociable du projet : JAMAIS de coupure moteur (AJ+STOP) hors
/// du circuit d'immobilisation, qui a sa propre approbation par un administrateur
/// de la société. Cet écran sert à la configuration (AJ+CONFN…, AJ+GO…), pas à
/// immobiliser. Le mot STOP est donc refusé quelle que soit sa position.</para>
///
/// <para>Le reste borne ce que le protocole NEMS et la colonne
/// device_commands.command_text (VARCHAR(100), retour à la ligne compris)
/// acceptent : une seule ligne, ASCII imprimable, préfixe AJ+.</para>
/// </summary>
public static class DeviceCommandSafety
{
    /// <summary>Taille de device_commands.command_text — le "\n" final compte.</summary>
    public const int MaxStoredLength = 100;

    private static readonly string[] Forbidden = { "STOP" };

    public static DeviceCommandCheck Check(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        text = text.TrimEnd('\r', '\n').Trim();

        if (text.Length == 0)
            return new DeviceCommandCheck(false, string.Empty, "Commande vide.");

        if (text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return new DeviceCommandCheck(false, string.Empty, "Une seule commande par envoi (une seule ligne).");

        foreach (var c in text)
        {
            if (c < 0x20 || c > 0x7E)
                return new DeviceCommandCheck(false, string.Empty, $"Caractère non autorisé (U+{(int)c:X4}) : ASCII imprimable uniquement.");
        }

        if (!text.StartsWith("AJ+", StringComparison.OrdinalIgnoreCase))
            return new DeviceCommandCheck(false, string.Empty, "Seules les commandes du protocole NEMS (préfixe AJ+) sont acceptées.");

        var upper = text.ToUpperInvariant();
        foreach (var word in Forbidden)
        {
            if (upper.Contains(word, StringComparison.Ordinal))
                return new DeviceCommandCheck(false, string.Empty,
                    "Commande refusée : la coupure moteur (STOP) ne passe jamais par cet écran — utilisez le circuit d'immobilisation, qui exige une approbation.");
        }

        var normalized = text + "\n";
        if (normalized.Length > MaxStoredLength)
            return new DeviceCommandCheck(false, string.Empty,
                $"Commande trop longue : {text.Length} caractères, maximum {MaxStoredLength - 1}.");

        return new DeviceCommandCheck(true, normalized, null);
    }
}
