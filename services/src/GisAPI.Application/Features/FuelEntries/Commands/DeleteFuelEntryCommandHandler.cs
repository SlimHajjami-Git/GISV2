using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public class DeleteFuelEntryCommandHandler : IRequestHandler<DeleteFuelEntryCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteFuelEntryCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(DeleteFuelEntryCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var entry = await _context.FuelEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == companyId, cancellationToken);

        if (entry == null)
            return false;

        // La création (CreateFuelEntryCommandHandler) et la lecture appliquent la portée,
        // pas la suppression : un locataire pouvait effacer le plein saisi sur le véhicule
        // d'un autre client en forgeant l'identifiant. Même réponse qu'un plein inexistant
        // (false => 404 côté contrôleur) : on ne révèle pas qu'il existe.
        // Un plein sans véhicule (saisie libre) n'entre dans la portée de personne :
        // seul un administrateur peut l'effacer.
        var autorise = entry.VehicleId.HasValue
            ? await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, entry.VehicleId.Value, cancellationToken)
            : VehicleScope.SeesWholeFleet(_tenantService);

        if (!autorise)
            return false;

        _context.FuelEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
