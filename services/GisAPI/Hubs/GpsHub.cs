using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GisAPI.Hubs;

[Authorize]
public class GpsHub : Hub
{
    private readonly ILogger<GpsHub> _logger;

    public GpsHub(ILogger<GpsHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        if (!string.IsNullOrEmpty(companyId))
        {
            // Add user to company group for multi-tenant isolation
            await Groups.AddToGroupAsync(Context.ConnectionId, $"company_{companyId}");
            _logger.LogInformation("Client {ConnectionId} joined company group {CompanyId}", 
                Context.ConnectionId, companyId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        if (!string.IsNullOrEmpty(companyId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"company_{companyId}");
            _logger.LogInformation("Client {ConnectionId} left company group {CompanyId}", 
                Context.ConnectionId, companyId);
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
}
