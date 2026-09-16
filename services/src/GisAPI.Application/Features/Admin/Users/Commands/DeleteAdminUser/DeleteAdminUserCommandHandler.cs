using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;

public class DeleteAdminUserCommandHandler : IRequestHandler<DeleteAdminUserCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteAdminUserCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteAdminUserCommand request, CancellationToken ct)
    {
        // Jusqu'ici tout compte connecté était insupprimable (23503) : se supprimer soi-même
        // l'était donc de fait. La suppression aboutissant désormais, on le refuse en clair.
        if (request.Id == _tenantService.UserId)
            throw new DomainException("Vous ne pouvez pas supprimer votre propre compte");

        // Lecture sans suivi : le compte est supprimé en SQL, un SaveChanges ultérieur
        // ne doit rien avoir à réécrire à son sujet.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Un Users.Remove simple levait 23503 (500) dès la première connexion du compte :
        // audit_logs et une douzaine d'autres tables le référencent sans cascade.
        await UserDeletionHelper.DeleteAsync(_context, user.Id, ct);
    }
}
