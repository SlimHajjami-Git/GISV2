using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.DeviceCommands;

/// <summary>
/// Historique des commandes envoyées depuis l'écran admin (source « admin » par
/// défaut ; passer une autre source, ou vide, pour tout voir). Lecture seule.
/// </summary>
public record GetAdminDeviceCommandHistoryQuery(int? CompanyId, int Limit, string? Source)
    : IRequest<List<AdminDeviceCommandHistoryDto>>;

public record AdminDeviceCommandHistoryDto(
    long Id,
    int DeviceId,
    string? Imei,
    string? Plate,
    string? VehicleName,
    int CompanyId,
    string? CompanyName,
    int UserId,
    string CommandType,
    string CommandText,
    string Status,
    string Source,
    int Attempts,
    DateTime? SentAt,
    DateTime CreatedAt,
    string? ErrorMessage);

public class GetAdminDeviceCommandHistoryQueryHandler
    : IRequestHandler<GetAdminDeviceCommandHistoryQuery, List<AdminDeviceCommandHistoryDto>>
{
    private readonly IGisDbContext _context;

    public GetAdminDeviceCommandHistoryQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminDeviceCommandHistoryDto>> Handle(GetAdminDeviceCommandHistoryQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit <= 0 ? 100 : request.Limit, 1, 1000);

        var query = _context.DeviceCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsQueryable();

        if (request.CompanyId.HasValue)
            query = query.Where(c => c.CompanyId == request.CompanyId.Value);

        var source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim();
        if (source != null && source != "*")
            query = query.Where(c => c.Source == source);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new AdminDeviceCommandHistoryDto(
                (long)c.Id,
                c.DeviceId,
                c.Device != null ? c.Device.DeviceUid : null,
                c.Vehicle != null ? c.Vehicle.Plate : null,
                c.Vehicle != null ? c.Vehicle.Name : null,
                c.CompanyId,
                c.Device != null && c.Device.Societe != null ? c.Device.Societe.Name : null,
                c.UserId,
                c.CommandType,
                c.CommandText,
                c.Status,
                c.Source,
                c.Attempts,
                c.SentAt,
                c.CreatedAt,
                c.ErrorMessage))
            .ToListAsync(ct);
    }
}
