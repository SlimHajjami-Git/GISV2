using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Employees.Commands.DeleteEmployee;

public record DeleteEmployeeCommand(int Id) : IRequest;

public class DeleteEmployeeCommandHandler : IRequestHandler<DeleteEmployeeCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteEmployeeCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteEmployeeCommand request, CancellationToken ct)
    {
        // Jusqu'ici tout compte connecté était insupprimable (23503) : se supprimer soi-même
        // l'était donc de fait. La suppression aboutissant désormais, on le refuse en clair.
        if (request.Id == _tenantService.UserId)
            throw new DomainException("Vous ne pouvez pas supprimer votre propre compte");

        // Filtre de société du DbContext, comme le FindAsync d'origine ; sans suivi, car le
        // compte est supprimé en SQL.
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct);
        if (user == null)
            throw new DomainException("Employé introuvable");

        // Cette route n'exige que le droit « Chauffeurs » : elle ne doit pas ôter un compte
        // administrateur, que seul l'écran Utilisateurs (droit « Utilisateurs ») gère.
        // L'erreur 23503 l'empêchait de fait tant que la suppression échouait.
        var adminRole = await _context.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Id == user.RoleId && (r.IsCompanyAdmin || r.IsSystemRole), ct);
        if (adminRole || user.AccessLevel == "admin")
            throw new ForbiddenAccessException("Un compte administrateur se supprime depuis l'écran Utilisateurs");

        // vehicles.assigned_driver_id n'est pas touché : c'est une clé étrangère vers
        // drivers(id), pas vers users(id). Filtrer sur user.Id retirait le chauffeur
        // du véhicule dont l'id drivers coïncidait avec celui de l'employé supprimé.

        // Un Users.Remove simple levait 23503 (500) dès la première connexion du compte :
        // audit_logs et une douzaine d'autres tables le référencent sans cascade.
        await UserDeletionHelper.DeleteAsync(_context, user.Id, ct);
    }
}
