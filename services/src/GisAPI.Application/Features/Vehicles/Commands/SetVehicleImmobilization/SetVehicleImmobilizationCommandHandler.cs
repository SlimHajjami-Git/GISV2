using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.SetVehicleImmobilization;

/// <summary>
/// Flips <c>Vehicle.IsImmobilized</c> on or off for the current tenant's
/// vehicle. Idempotent: activating an already-immobilised vehicle just
/// refreshes the reason and the actor; deactivating a non-immobilised
/// one is a no-op that still returns the current state.
///
/// <para>Both monitoring services (<c>BatteryMonitoringService</c>,
/// <c>VoltageHealthMonitoringService</c>) and the real-time alert path
/// in <c>BroadcastPositionCommandHandler</c> read this flag at next
/// cycle / next frame, so the change takes effect within the broadcast
/// handler's 10-minute cache TTL — no manual cache flush needed.</para>
/// </summary>
public class SetVehicleImmobilizationCommandHandler
    : IRequestHandler<SetVehicleImmobilizationCommand, SetVehicleImmobilizationResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SetVehicleImmobilizationCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<SetVehicleImmobilizationResult> Handle(
        SetVehicleImmobilizationCommand request,
        CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId
                                   && v.CompanyId == companyId, ct);

        if (vehicle == null)
            throw new NotFoundException("Vehicle", request.VehicleId);

        if (request.Activate)
        {
            vehicle.IsImmobilized = true;
            // Only stamp StartedAt on the FIRST activation — if the
            // operator re-submits the form (e.g. to update the reason),
            // we keep the original start date for the "immobilised since"
            // copy on the UI.
            if (vehicle.ImmobilizationStartedAt == null)
            {
                vehicle.ImmobilizationStartedAt = DateTime.UtcNow;
                vehicle.ImmobilizationStartedByUserId = currentUserId;
            }
            vehicle.ImmobilizationReason = string.IsNullOrWhiteSpace(request.Reason)
                ? null
                : request.Reason.Trim();
        }
        else
        {
            vehicle.IsImmobilized = false;
            vehicle.ImmobilizationReason = null;
            vehicle.ImmobilizationStartedAt = null;
            vehicle.ImmobilizationStartedByUserId = null;
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new SetVehicleImmobilizationResult(
            vehicle.Id,
            vehicle.IsImmobilized,
            vehicle.ImmobilizationReason,
            vehicle.ImmobilizationStartedAt,
            vehicle.ImmobilizationStartedByUserId);
    }
}
