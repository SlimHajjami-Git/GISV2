using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users;

/// <summary>
/// Unicité de l'e-mail de connexion, au même périmètre que la base. Deux écarts vécus
/// en production tenaient au fait que chaque écran réécrivait ce contrôle à sa façon :
///
/// 1. L'index `idx_users_email` est UNIQUE sur TOUTE la table, alors que `_context.Users`
///    est filtré par société. Un contrôle écrit sans `IgnoreQueryFilters()` ne voit pas
///    l'adresse d'une AUTRE société : il laisse passer, PostgreSQL rend un 23505, et le
///    client lit « An unexpected error occurred » sans jamais savoir quoi corriger
///    (HERTZ, 03/10/2026, avec une adresse déjà prise par PARENIN).
/// 2. La connexion compare l'e-mail SANS tenir compte de la casse
///    (`LoginCommandHandler`) tandis que l'index, lui, la distingue. `A@x.tn` et
///    `a@x.tn` pourraient donc coexister et la connexion en servirait un au hasard.
///    On refuse donc la collision dès la saisie, plus strictement que l'index.
///
/// Le message reste volontairement neutre : il dit que l'adresse est prise, jamais par
/// qui ni dans quelle société.
/// </summary>
public static class UserEmailRules
{
    /// <summary>Forme enregistrée en base : sans espaces de bord, casse conservée.</summary>
    public static string Normalize(string? email) => (email ?? string.Empty).Trim();

    /// <summary>
    /// Refuse l'adresse si un compte la porte déjà, toutes sociétés confondues.
    /// <paramref name="exceptUserId"/> exclut le compte en cours de modification, sans
    /// quoi enregistrer sa fiche sans toucher à l'e-mail serait refusé.
    /// </summary>
    public static async Task EnsureAvailableAsync(
        IGisDbContext context, string? email, int? exceptUserId, CancellationToken ct)
    {
        var normalise = Normalize(email);
        if (normalise.Length == 0) return;   // champ obligatoire : c'est la validation qui le dit

        var minuscules = normalise.ToLower();
        var requete = context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Email.ToLower() == minuscules);

        if (exceptUserId is int sien)
            requete = requete.Where(u => u.Id != sien);

        if (await requete.AnyAsync(ct))
            throw new ConflictException("Cet email est déjà utilisé");
    }
}
