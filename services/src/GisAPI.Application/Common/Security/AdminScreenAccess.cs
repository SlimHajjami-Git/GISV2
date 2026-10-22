using System.Text.Json;
using System.Text.RegularExpressions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Administrateur RESTREINT de la plateforme (demande du 22/10/2026, compte d'Adel) : un rôle
/// système dont les permissions portent la liste des écrans d'administration autorisés
/// (<see cref="PermissionKey"/>, migration 063). Sans cette clé, l'administrateur garde
/// TOUT, comme avant.
///
/// <para>Masquer le menu ne suffisait pas : toute route /api/admin était ouverte à tout
/// administrateur système. La garde serveur (PermissionMiddleware) applique donc une liste
/// BLANCHE : seules les fonctions des écrans autorisés passent, et jamais l'irréversible
/// (suppressions, réinitialisation des données d'une société, choix C de Karim). « Se
/// connecter en tant que » est refusé aussi : sous l'identité d'un client, il pourrait
/// supprimer ce que son propre rôle lui interdit.</para>
///
/// <para>Hors /api/admin, un administrateur système voit toutes les sociétés (filtres de
/// tenant levés) : l'écran Monitoring réutilise le monitoring client. Ces routes sont donc
/// fermées elles aussi, sauf les lectures du monitoring et le compte de l'appelant.</para>
/// </summary>
public static class AdminScreenAccess
{
    /// <summary>Clé des permissions du rôle : tableau JSON (ou liste séparée par des virgules) d'écrans.</summary>
    public const string PermissionKey = "admin_screens";

    public const string Clients = "clients";
    public const string Users = "users";
    public const string Vehicles = "vehicles";
    public const string Monitoring = "monitoring";

    public const string RefusalCode = "ADMIN_RESTRICTED";
    public const string RefusalMessage = "Cette action n'est pas autorisée pour votre compte d'administration.";

    /// <summary>
    /// Écrans autorisés pour ce rôle, ou null = accès complet (administrateur sans restriction,
    /// ou rôle qui n'est pas un rôle système).
    /// </summary>
    public static IReadOnlyList<string>? AllowedScreens(Role? role)
    {
        if (role is not { IsSystemRole: true } || role.Permissions == null
            || !role.Permissions.TryGetValue(PermissionKey, out var value) || value == null)
            return null;

        IEnumerable<string> screens = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } array =>
                array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!),
            JsonElement { ValueKind: JsonValueKind.String } text => Split(text.GetString()),
            string text => Split(text),
            IEnumerable<object> items => items.Select(i => i?.ToString() ?? ""),
            _ => Split(value.ToString())
        };
        return screens.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();

        static IEnumerable<string> Split(string? text) => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed record Rule(string Methods, Regex Path, string? Screen);

    private static Rule R(string methods, string pattern, string? screen) =>
        new(methods, new Regex("^" + pattern + "$", RegexOptions.Compiled | RegexOptions.CultureInvariant), screen);

    private const string Id = @"\d+";

    /// <summary>
    /// Fonctions d'administration par écran (relevé des appels des écrans le 22/10/2026).
    /// Screen null = utile à toute la console (cookie des fichiers). Aucune suppression ici,
    /// ni la réinitialisation des données d'une société.
    /// </summary>
    private static readonly Rule[] AdminRules =
    {
        R("POST", "/api/admin/fichiers", null),

        // Sociétés (liste et fiche).
        R("GET", "/api/admin/billing/overview", Clients),
        R("GET", "/api/admin/subscription-types", Clients),
        R("GET POST", "/api/admin/company", Clients),
        R("GET PUT", $"/api/admin/company/{Id}", Clients),
        R("POST", $"/api/admin/company/{Id}/(activate|suspend|mark-paid)", Clients),
        R("PUT", $"/api/admin/company/{Id}/(auto-suspend|subscription-expiry)", Clients),
        R("GET", $"/api/admin/company/{Id}/(roles|users|vehicles)", Clients),
        R("GET", $"/api/admin/societes/{Id}", Clients),
        R("PUT", $"/api/admin/societes/{Id}/scan-quota", Clients),

        // Users : liste des comptes, avec les sociétés et leurs rôles pour les filtres.
        R("GET", "/api/admin/company", Users),
        R("GET", $"/api/admin/company/{Id}/roles", Users),
        R("GET", $"/api/admin/users(/available-permissions|/available-roles|/stats|/{Id})?", Users),
        R("POST", "/api/admin/users", Users),
        R("PUT", $"/api/admin/users/{Id}(/role)?", Users),
        R("POST", $"/api/admin/users/{Id}/(activate|suspend|reset-password)", Users),

        // Véhicules.
        R("GET", "/api/admin/company", Vehicles),
        R("GET", $"/api/admin/vehicles(/with-positions|/{Id}|/{Id}/history)?", Vehicles),
        R("POST", "/api/admin/vehicles", Vehicles),
        R("PUT", $"/api/admin/vehicles/{Id}", Vehicles),
        R("POST", $"/api/admin/vehicles/{Id}/replace-device", Vehicles),

        // Monitoring de la console : carte de toute la flotte et historique d'un véhicule.
        R("GET", $"/api/admin/vehicles(/with-positions|/{Id}/history)", Monitoring),
    };

    /// <summary>Routes hors /api/admin : le compte de l'appelant, les rôles de la fiche société, les lectures du monitoring.</summary>
    private static readonly Rule[] OtherRules =
    {
        R("POST", "/api/auth/(login|refresh|logout|change-password)", null),
        R("GET PUT", "/api/users/me(/.*)?", null),
        R("GET POST PUT", "/api/notifications(/.*)?", null),
        // Référentiel des marques et modèles (lecture seule, sans société) : formulaires véhicule.
        R("GET", "/api/brands(/.*)?", null),

        // Fiche société : création et modification des rôles (jamais leur suppression).
        R("GET POST", "/api/roles", Clients),
        R("GET PUT", $"/api/roles/{Id}", Clients),

        // Monitoring : lectures de la carte, de l'historique et des zones ; flux temps réel.
        R("GET", "/api/(vehicles|gps|geofences|geocoding|drivers|alerts|positions)(/.*)?", Monitoring),
        R("GET POST", "/api/hubs/.*", Monitoring),
    };

    /// <summary>
    /// L'appel (méthode, chemin en minuscules) est-il permis à un administrateur restreint
    /// ayant ces écrans ? Hors /api/ (pages, hubs historiques, santé) : rien à restreindre ici.
    /// </summary>
    public static bool CanCall(IReadOnlyCollection<string> screens, string method, string path)
    {
        path = path.ToLowerInvariant().TrimEnd('/');
        method = method.ToUpperInvariant();
        if (!path.StartsWith("/api/")) return true;
        if (method is "OPTIONS" or "HEAD") method = "GET";

        var rules = path.StartsWith("/api/admin/") || path == "/api/admin" ? AdminRules : OtherRules;
        return rules.Any(r =>
            r.Methods.Split(' ').Contains(method)
            && r.Path.IsMatch(path)
            && (r.Screen == null || screens.Contains(r.Screen)));
    }

    /// <summary>
    /// Ce que la liste blanche des routes ne peut PAS exprimer : l'écran Users est autorisé,
    /// mais la société des administrateurs de la plateforme y figure comme les autres. Sans
    /// cette règle, un administrateur restreint pouvait se donner le rôle « Administrateur »
    /// complet (même société, donc accepté par UpdateAdminUserRole), créer un compte qui le
    /// porte, ou réinitialiser le mot de passe d'un administrateur complet — et sortir de sa
    /// restriction en un appel. Règle : un administrateur restreint ne touche jamais un compte
    /// à rôle système et n'attribue jamais un rôle système.
    /// </summary>
    public static void EnsureMayManage(IReadOnlyCollection<string>? callerScreens, bool targetHasSystemRole, bool newRoleIsSystem)
    {
        if (callerScreens == null) return;
        if (targetHasSystemRole || newRoleIsSystem)
            throw new ForbiddenAccessException(RefusalMessage);
    }

    /// <summary>
    /// Version branchée sur la base : relit le rôle de l'appelant (comme PermissionMiddleware,
    /// pour qu'un changement de rôle prenne effet tout de suite), puis applique
    /// <see cref="EnsureMayManage"/>. Appelant inconnu ou sans restriction : rien à faire.
    /// </summary>
    public static async Task EnsureCallerMayManageAsync(
        IGisDbContext db, int? callerUserId, int? targetUserId, int? newRoleId, CancellationToken ct)
    {
        if (callerUserId is not int callerId) return;

        var callerRole = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == callerId)
            .Select(u => u.Role)
            .FirstOrDefaultAsync(ct);
        var screens = AllowedScreens(callerRole);
        if (screens == null) return;

        var targetHasSystemRole = targetUserId is int targetId
            && await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == targetId)
                .Select(u => u.Role != null && u.Role.IsSystemRole)
                .FirstOrDefaultAsync(ct);

        var newRoleIsSystem = newRoleId is int roleId
            && await db.Roles.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.Id == roleId)
                .Select(r => r.IsSystemRole)
                .FirstOrDefaultAsync(ct);

        EnsureMayManage(screens, targetHasSystemRole, newRoleIsSystem);
    }
}
