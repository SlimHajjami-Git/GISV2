using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record DeleteDriverCommand(int Id) : IRequest;

public class DeleteDriverCommandHandler : IRequestHandler<DeleteDriverCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Chauffeur", request.Id);

        // Fiche reliée à un compte chauffeur (migration 050) : supprimer la fiche ferme
        // l'accès à l'application — sinon le compte, toujours actif, gardait une session
        // de 90 jours sans plus aucune tournée à recevoir. Le compte n'est pas supprimé
        // (audit, historique) ; il apparaît inactif dans l'écran Utilisateurs.
        if (driver.UserId is int compteId)
        {
            var compte = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == compteId && u.CompanyId == companyId, ct);
            if (compte != null && compte.IsDriverAccount)
                await Users.DriverAccountRules.RevokeAccessAsync(_context, compte, ct);
        }

        _context.Drivers.Remove(driver);
        await _context.SaveChangesAsync(ct);
    }
}
