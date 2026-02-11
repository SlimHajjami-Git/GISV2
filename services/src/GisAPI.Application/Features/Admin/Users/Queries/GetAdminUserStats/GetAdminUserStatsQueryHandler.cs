using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Queries.GetAdminUserStats;

public class GetAdminUserStatsQueryHandler : IRequestHandler<GetAdminUserStatsQuery, UserStatsDto>
{
    private readonly IGisDbContext _context;

    public GetAdminUserStatsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<UserStatsDto> Handle(GetAdminUserStatsQuery request, CancellationToken ct)
    {
        var totalUsers = await _context.Users.IgnoreQueryFilters().CountAsync(ct);
        var activeUsers = await _context.Users.IgnoreQueryFilters().CountAsync(u => u.Status == "active", ct);
        var suspendedUsers = await _context.Users.IgnoreQueryFilters().CountAsync(u => u.Status == "suspended", ct);
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-15);
        var onlineUsers = await _context.Users.IgnoreQueryFilters()
            .CountAsync(u => u.LastLoginAt != null && u.LastLoginAt > onlineThreshold, ct);

        var usersByCompany = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .GroupBy(u => new { u.CompanyId, CompanyName = u.Societe != null ? u.Societe.Name : "Unknown" })
            .Select(g => new CompanyUserCount
            {
                CompanyId = g.Key.CompanyId,
                CompanyName = g.Key.CompanyName,
                UserCount = g.Count()
            })
            .OrderByDescending(x => x.UserCount)
            .Take(10)
            .ToListAsync(ct);

        return new UserStatsDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            SuspendedUsers = suspendedUsers,
            OnlineUsers = onlineUsers,
            UsersByCompany = usersByCompany
        };
    }
}
