using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Dashboard;

public record GetDashboardStatsQuery() : IRequest<DashboardStatsDto>;

public class DashboardStatsDto
{
    public int TotalClients { get; set; }
    public int ActiveClients { get; set; }
    public int TotalUsers { get; set; }
    public int UsersOnline { get; set; }
    public int TotalVehicles { get; set; }
    public int ActiveDevices { get; set; }
    public int TotalPositionsToday { get; set; }
    public int AlertsToday { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int NewClientsThisMonth { get; set; }
}

public class GetDashboardStatsQueryHandler : IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>
{
    private readonly IGisDbContext _context;

    public GetDashboardStatsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var firstOfMonth = new DateTime(today.Year, today.Month, 1);

        return new DashboardStatsDto
        {
            TotalClients = await _context.Societes.CountAsync(ct),
            ActiveClients = await _context.Societes.CountAsync(c => c.IsActive, ct),
            TotalUsers = await _context.Users.CountAsync(ct),
            UsersOnline = 0,
            TotalVehicles = await _context.Vehicles.CountAsync(ct),
            ActiveDevices = await _context.GpsDevices.CountAsync(ct),
            TotalPositionsToday = await _context.GpsPositions.CountAsync(p => p.RecordedAt >= today, ct),
            AlertsToday = 0,
            RevenueThisMonth = 0,
            NewClientsThisMonth = await _context.Societes.CountAsync(c => c.CreatedAt >= firstOfMonth, ct)
        };
    }
}
