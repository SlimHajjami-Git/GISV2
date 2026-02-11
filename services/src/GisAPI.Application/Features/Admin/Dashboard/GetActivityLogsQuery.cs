using GisAPI.Application.Common.Interfaces;
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
    private readonly IGisDbContext _context;

    public GetActivityLogsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<ActivityLogDto>> Handle(GetActivityLogsQuery request, CancellationToken ct)
    {
        var recentLogins = await _context.Users
            .Where(u => u.LastLoginAt != null)
            .OrderByDescending(u => u.LastLoginAt)
            .Take(request.Limit)
            .Select(u => new ActivityLogDto
            {
                Id = $"login-{u.Id}",
                UserId = u.Id,
                UserName = u.Name,
                CompanyId = u.CompanyId,
                CompanyName = u.Societe != null ? u.Societe.Name : "Unknown",
                Action = "login",
                Details = "Connexion utilisateur",
                IpAddress = "N/A",
                Timestamp = u.LastLoginAt ?? DateTime.UtcNow
            })
            .ToListAsync(ct);

        var recentVehicles = await _context.Vehicles
            .OrderByDescending(v => v.UpdatedAt)
            .Take(20)
            .Select(v => new ActivityLogDto
            {
                Id = $"vehicle-{v.Id}",
                UserId = 0,
                UserName = "System",
                CompanyId = v.CompanyId,
                CompanyName = v.Societe != null ? v.Societe.Name : "Unknown",
                Action = "update_vehicle",
                Details = $"Mise à jour véhicule: {v.Plate}",
                IpAddress = "N/A",
                Timestamp = v.UpdatedAt
            })
            .ToListAsync(ct);

        return recentLogins.Concat(recentVehicles)
            .OrderByDescending(l => l.Timestamp)
            .Take(request.Limit)
            .ToList();
    }
}
