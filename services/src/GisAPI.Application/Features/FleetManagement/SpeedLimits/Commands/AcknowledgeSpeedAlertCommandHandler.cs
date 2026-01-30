using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

public class AcknowledgeSpeedAlertCommandHandler : IRequestHandler<AcknowledgeSpeedAlertCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public AcknowledgeSpeedAlertCommandHandler(
        IGisDbContext context, 
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(AcknowledgeSpeedAlertCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var alert = await _context.SpeedLimitAlerts
            .FirstOrDefaultAsync(a => a.Id == request.AlertId && a.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException($"Speed alert with ID {request.AlertId} not found");

        if (alert.IsAcknowledged)
            throw new InvalidOperationException("Alert has already been acknowledged");

        alert.IsAcknowledged = true;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedById = _tenantService.UserId;

        await _context.SaveChangesAsync(cancellationToken);
    }
}



