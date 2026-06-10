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
            UserName = a.User != null ? a.User.Name : (a.EntityName ?? "Système"),
            CompanyId = a.CompanyId ?? 0,
            CompanyName = a.CompanyId != null && companyNames.TryGetValue(a.CompanyId.Value, out var cn) ? cn : "Unknown",
            Action = a.Action,
            Details = string.IsNullOrEmpty(a.Description) ? a.Action : a.Description,
            IpAddress = string.IsNullOrEmpty(a.IpAddress) ? "N/A" : a.IpAddress,
            Timestamp = a.Timestamp
        }).ToList();
    }
}
