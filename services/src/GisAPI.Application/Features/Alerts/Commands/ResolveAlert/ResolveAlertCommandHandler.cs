using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Alerts.Commands.ResolveAlert;

public class ResolveAlertCommandHandler : IRequestHandler<ResolveAlertCommand, bool>
{
    private readonly IGisDbContext _context;

    public ResolveAlertCommandHandler(IGisDbContext context) => _context = context;

    public async Task<bool> Handle(ResolveAlertCommand request, CancellationToken ct)
    {
        var alert = await _context.GpsAlerts.FindAsync(new object[] { request.AlertId }, ct);
        if (alert == null) return false;

        alert.Resolved = true;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedByUserId = request.UserId;

        await _context.SaveChangesAsync(ct);
        return true;
    }
}
