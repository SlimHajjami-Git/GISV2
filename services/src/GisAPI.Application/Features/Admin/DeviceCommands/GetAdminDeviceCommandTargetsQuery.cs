using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.FleetManagement.SpeedLimits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.DeviceCommands;

/// <summary>Les boîtiers d'une société, tels que l'écran admin les propose à la sélection.</summary>
public record GetAdminDeviceCommandTargetsQuery(int CompanyId) : IRequest<List<AdminDeviceCommandTargetDto>>;

public record AdminDeviceCommandTargetDto(
    int DeviceId,
    string Imei,
    string? Mat,
    string? Label,
    int? VehicleId,
    string? Plate,
    string? VehicleName,
    string? ProtocolType,
    string? Brand,
    string? Model,
    string? FirmwareVersion,
    bool IsNems,
    DateTime? LastCommunication,
    bool OnlineRecently,
    string Status);

public class GetAdminDeviceCommandTargetsQueryHandler
    : IRequestHandler<GetAdminDeviceCommandTargetsQuery, List<AdminDeviceCommandTargetDto>>
{
    /// <summary>Au-delà, le push instantané échouera presque sûrement (socket fermé).</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(10);

    private readonly IGisDbContext _context;

    public GetAdminDeviceCommandTargetsQueryHandler(IGisDbContext context) => _context = context;

    public async Task<List<AdminDeviceCommandTargetDto>> Handle(GetAdminDeviceCommandTargetsQuery request, CancellationToken ct)
    {
        var devices = await _context.GpsDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(d => d.Vehicle)
            .Where(d => d.CompanyId == request.CompanyId)
            .ToListAsync(ct);

        var threshold = DateTime.UtcNow - OnlineWindow;

        return devices
            .Select(d => new AdminDeviceCommandTargetDto(
                d.Id,
                d.DeviceUid,
                d.Mat,
                d.Label,
                d.Vehicle?.Id,
                d.Vehicle?.Plate,
                d.Vehicle?.Name,
                d.ProtocolType,
                d.Brand,
                d.Model,
                d.FirmwareVersion,
                SpeedLimitCommandBuilder.IsNemsDevice(d),
                d.LastCommunication,
                d.LastCommunication.HasValue && d.LastCommunication.Value >= threshold,
                d.Status))
            .OrderBy(t => t.Plate ?? t.Label ?? t.Imei)
            .ToList();
    }
}
