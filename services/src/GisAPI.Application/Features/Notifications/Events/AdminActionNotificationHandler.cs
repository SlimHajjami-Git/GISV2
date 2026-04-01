using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class AdminActionNotificationHandler : INotificationHandler<AdminActionNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<AdminActionNotificationHandler> _logger;

    public AdminActionNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<AdminActionNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(AdminActionNotificationEvent e, CancellationToken ct)
    {
        try
        {
            // Find company admin users (exclude the actor themselves)
            var adminUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active" && u.Id != e.ActorUserId)
                .Include(u => u.Role)
                .Where(u => u.Role != null && u.Role.IsCompanyAdmin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (adminUsers.Count == 0) return;

            var (title, message, actionUrl) = e.ActionType switch
            {
                "vehicle_created" => (
                    "Nouveau véhicule ajouté",
                    $"{e.ActorName} a ajouté le véhicule \"{e.EntityName}\"",
                    "/vehicles"
                ),
                "driver_created" => (
                    "Nouveau chauffeur ajouté",
                    $"{e.ActorName} a ajouté le chauffeur \"{e.EntityName}\"",
                    "/drivers"
                ),
                "user_created" => (
                    "Nouvel utilisateur créé",
                    $"{e.ActorName} a créé l'utilisateur \"{e.EntityName}\"",
                    "/users"
                ),
                "maintenance_created" => (
                    "Maintenance enregistrée",
                    $"{e.ActorName} a enregistré une maintenance pour \"{e.EntityName}\"",
                    "/maintenance"
                ),
                "cost_created" => (
                    $"Dépense enregistrée : {e.EntityName}",
                    $"{e.ActorName} a enregistré une dépense : \"{e.EntityName}\"",
                    "/costs"
                ),
                "document_created" => (
                    "Document ajouté",
                    $"{e.ActorName} a ajouté un document pour \"{e.EntityName}\"",
                    "/documents"
                ),
                "accident_created" => (
                    "Sinistre déclaré",
                    $"{e.ActorName} a déclaré un sinistre pour \"{e.EntityName}\"",
                    "/accidents"
                ),
                "geofence_created" => (
                    "Géofence créée",
                    $"{e.ActorName} a créé la géofence \"{e.EntityName}\"",
                    "/geofences"
                ),
                "supplier_created" => (
                    "Fournisseur ajouté",
                    $"{e.ActorName} a ajouté le fournisseur \"{e.EntityName}\"",
                    "/suppliers"
                ),
                _ => (
                    "Activité employé",
                    $"{e.ActorName} a effectué une action: {e.ActionType}",
                    "/dashboard"
                )
            };

            foreach (var userId in adminUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "admin_action",
                    title: title,
                    message: message,
                    priority: "normal",
                    referenceType: e.EntityType,
                    referenceId: e.EntityId,
                    actionUrl: actionUrl,
                    metadata: new Dictionary<string, object>
                    {
                        { "actionType", e.ActionType },
                        { "actorName", e.ActorName },
                        { "actorUserId", e.ActorUserId }
                    },
                    ct: ct
                );
            }

            _logger.LogDebug("Admin action notification sent to {Count} admins: {Actor} — {Action} — {Entity}",
                adminUsers.Count, e.ActorName, e.ActionType, e.EntityName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create admin action notification");
        }
    }
}
