using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Dashboard;

public record GetActivityLogsQuery(int Limit = 50) : IRequest<List<ActivityLogDto>>;

public class ActivityLogDto
{
    public string Id { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class GetActivityLogsQueryHandler : IRequestHandler<GetActivityLogsQuery, List<ActivityLogDto>>
{
    public const string DeletedUserLabel = "Utilisateur supprimé";
    public const string SystemLabel = "Système";

    // Actions que leurs écrivains inscrivent TOUJOURS avec l'utilisateur qui agit : handlers
    // d'authentification (login, logout, session), prise de POV, remise à zéro d'une société,
    // correction de kilométrage, et AuditTrailMiddleware (creation_/modification_/suppression_,
    // requêtes authentifiées seulement). Sans UserId, le compte a été supprimé depuis :
    // UserDeletionHelper détache le journal au lieu de le purger. Les écrivains déclarent
    // ces noms en littéraux : ActivityLogAuthorTests relève chaque écriture d'AuditLog du
    // code source et échoue si son action n'est pas classée ici.
    private static readonly HashSet<string> UserActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "logout", "session", "impersonate", "company_data_reset", "vehicle_mileage_corrected",
    };

    private static readonly string[] UserActionPrefixes = { "creation_", "modification_", "suppression_" };

    private readonly IGisDbContext _context;

    public GetActivityLogsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<ActivityLogDto>> Handle(GetActivityLogsQuery request, CancellationToken ct)
    {
        // Read the real audit trail (login / logout / recorded actions), most recent first.
        // Tenant-scoped via the AuditLog global query filter (company-admin sees own company,
        // system-admin sees all).
        var rows = await _context.AuditLogs
            .Include(a => a.User)
            .OrderByDescending(a => a.Timestamp)
            .Take(request.Limit)
            .ToListAsync(ct);

        var companyIds = rows.Where(a => a.CompanyId != null)
            .Select(a => a.CompanyId!.Value)
            .Distinct()
            .ToList();
        var companyNames = await _context.Societes
            .Where(s => companyIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        return rows.Select(a => new ActivityLogDto
        {
            Id = a.Id.ToString(),
            UserId = a.UserId ?? 0,
            UserName = ResolveUserName(a),
            CompanyId = a.CompanyId ?? 0,
            CompanyName = a.CompanyId != null && companyNames.TryGetValue(a.CompanyId.Value, out var cn) ? cn : "Unknown",
            Action = a.Action,
            Details = string.IsNullOrEmpty(a.Description) ? a.Action : a.Description,
            IpAddress = string.IsNullOrEmpty(a.IpAddress) ? "N/A" : a.IpAddress,
            Timestamp = a.Timestamp
        }).ToList();
    }

    /// <summary>
    /// Auteur affiché d'une ligne du journal. EntityName désigne l'OBJET de l'action (plaque,
    /// société, compte visé) : repris comme auteur, une correction de kilométrage d'un compte
    /// supprimé semblait faite par le véhicule. Seul le refus de connexion, écrit sans UserId
    /// pour ne pas rendre le compte insupprimable, a pour seule identité l'adresse tapée
    /// (masquée si inconnue), qu'il faut garder.
    /// </summary>
    public static string ResolveUserName(AuditLog log)
    {
        if (log.User != null) return log.User.Name;

        if (string.Equals(log.Action, LoginCommandHandler.FailedLoginAction, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(log.EntityName) ? "Adresse inconnue" : log.EntityName;

        // Compte référencé mais non chargé (hors du périmètre lisible) : ni supprimé, ni système.
        if (log.UserId is > 0) return $"Utilisateur n° {log.UserId}";

        return IsUserAction(log.Action) ? DeletedUserLabel : SystemLabel;
    }

    private static bool IsUserAction(string? action) =>
        !string.IsNullOrEmpty(action)
        && (UserActions.Contains(action)
            || UserActionPrefixes.Any(p => action.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
}
