using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.DeleteUser;

public class DeleteUserCommandHandler : IRequestHandler<DeleteUserCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteUserCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        if (request.Id == currentUserId)
            throw new DomainException("Vous ne pouvez pas supprimer votre propre compte");

        // Lecture sans suivi : le compte est supprimé en SQL, un SaveChanges ultérieur
        // ne doit rien avoir à réécrire à son sujet.
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Tant que la suppression échouait en 23503, un compte qui s'était connecté était
        // protégé de fait. Le droit « Utilisateurs » ne doit pas pour autant ôter un compte
        // système ni laisser la société sans administrateur.
        var role = await _context.Roles
            .AsNoTracking()
            .Where(r => r.Id == user.RoleId)
            .Select(r => new { r.IsCompanyAdmin, r.IsSystemRole })
            .FirstOrDefaultAsync(ct);
        if (role?.IsSystemRole == true && !_tenantService.IsSystemAdmin)
            throw new ForbiddenAccessException("Un compte administrateur système ne peut pas être supprimé depuis cet écran");

        var isCompanyAdmin = role?.IsCompanyAdmin == true || user.AccessLevel == "admin";
        if (isCompanyAdmin)
        {
            // Un administrateur restant doit pouvoir se CONNECTER : LoginCommandHandler refuse
            // tout compte dont le statut n'est pas « active ». Un admin suspendu ou en attente
            // ne sauvait pas la société de se retrouver sans administrateur.
            var otherAdmin = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.CompanyId == companyId && u.Id != user.Id && u.Status == "active"
                            && (u.AccessLevel == "admin" || u.Role.IsCompanyAdmin), ct);
            if (!otherAdmin)
                throw new DomainException("Impossible de supprimer le dernier administrateur de la société");
        }

        // Un Users.Remove simple levait 23503 (500) dès la première connexion du compte :
        // audit_logs et une douzaine d'autres tables le référencent sans cascade.
        await UserDeletionHelper.DeleteAsync(_context, user.Id, ct);
    }
}
