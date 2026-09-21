using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace GisAPI.Hubs;

[Authorize]
public class GpsHub : Hub
{
    private readonly ILogger<GpsHub> _logger;
    private readonly IGisDbContext _context;

    public GpsHub(ILogger<GpsHub> logger, IGisDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public override async Task OnConnectedAsync()
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Compte chauffeur (migration 050) : jamais sur le hub de la flotte — le groupe
        // company_{id} diffuse les positions de tous les véhicules, et SubscribeToVehicle
        // ne vérifie pas l'appartenance. Défense en profondeur : PermissionMiddleware refuse
        // déjà /hubs/* et /api/hubs/* à un jeton « chauffeur ». Mais un salarié converti
        // garde un jeton SANS ce claim jusqu'à son expiration (24 h) : la ligne en base
        // tranche aussi, lue une fois à la connexion (pas à chaque message).
        if (Context.User?.FindFirst(JwtClaims.AccountType)?.Value == UserAccountTypes.Driver
            || await IsDriverAccountInDatabaseAsync(userId))
        {
            _logger.LogWarning("Hub GPS refusé à un compte chauffeur ({ConnectionId})", Context.ConnectionId);
            Context.Abort();
            return;
        }

        if (!string.IsNullOrEmpty(companyId))
        {
            // Add user to company group for multi-tenant isolation
            await Groups.AddToGroupAsync(Context.ConnectionId, $"company_{companyId}");
            _logger.LogInformation("Client {ConnectionId} joined company group {CompanyId}", 
                Context.ConnectionId, companyId);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            // Add user to personal notification group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            _logger.LogDebug("Client {ConnectionId} joined user group {UserId}", 
                Context.ConnectionId, userId);
        }

        await base.OnConnectedAsync();
    }

    private async Task<bool> IsDriverAccountInDatabaseAsync(string? userId)
    {
        if (!int.TryParse(userId, out var id)) return false;
        var accountType = await _context.Users
            .IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.AccountType)
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        return accountType == UserAccountTypes.Driver;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!string.IsNullOrEmpty(companyId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"company_{companyId}");
            _logger.LogInformation("Client {ConnectionId} left company group {CompanyId}", 
                Context.ConnectionId, companyId);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Subscribe to specific vehicle updates
    public async Task SubscribeToVehicle(int vehicleId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"vehicle_{vehicleId}");
        _logger.LogDebug("Client {ConnectionId} subscribed to vehicle {VehicleId}", 
            Context.ConnectionId, vehicleId);
    }

    public async Task UnsubscribeFromVehicle(int vehicleId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"vehicle_{vehicleId}");
    }

    // Subscribe to geofence events
    public async Task SubscribeToGeofence(int geofenceId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"geofence_{geofenceId}");
    }

    public async Task UnsubscribeFromGeofence(int geofenceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"geofence_{geofenceId}");
    }

    // Chat: notify a user that someone is typing
    public async Task ChatTyping(int receiverId)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            await Clients.Group($"user_{receiverId}").SendAsync("ChatTyping", new { SenderId = int.Parse(userId) });
        }
    }
}

// Implementation of the Application layer interface
public class GpsHubService : GisAPI.Application.Common.Interfaces.IGpsHubService
{
    private readonly IHubContext<GpsHub> _hubContext;

    public GpsHubService(IHubContext<GpsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendPositionUpdateAsync(int companyId, object position)
    {
        Console.WriteLine($"📡 GpsHubService: Sending PositionUpdate to company_{companyId}");
        await _hubContext.Clients.Group($"company_{companyId}")
            .SendAsync("PositionUpdate", position);
    }

    public async Task SendVehiclePositionAsync(int vehicleId, object position)
    {
        await _hubContext.Clients.Group($"vehicle_{vehicleId}")
            .SendAsync("VehiclePosition", position);
    }

    public async Task SendAlertAsync(int companyId, object alert)
    {
        await _hubContext.Clients.Group($"company_{companyId}")
            .SendAsync("Alert", alert);
    }

    public async Task SendGeofenceEventAsync(int geofenceId, object geofenceEvent)
    {
        await _hubContext.Clients.Group($"geofence_{geofenceId}")
            .SendAsync("GeofenceEvent", geofenceEvent);
    }

    public async Task SendSubscriptionChangedAsync(int companyId, string status)
    {
        await _hubContext.Clients.Group($"company_{companyId}")
            .SendAsync("SubscriptionChanged", new { status });
    }
}



