using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Employees.Queries.GetEmployees;

public record GetEmployeesQuery(string? Role = null, string? Status = null) : IRequest<List<EmployeeDto>>;

public record EmployeeDto(
    int Id,
    string FirstName,
    string LastName,
    string Name,
    string Email,
    string? Phone,
    string? EmployeeRole,
    string Status,
    string? PermitNumber,
    string? PermitType,
    DateTime? PermitExpiry,
    string? CIN,
    DateTime? DateOfBirth,
    DateTime? HireDate,
    int? AssignedVehicleId,
    string? AssignedVehicleName,
    string? AssignedVehiclePlate,
    int[]? AssignedVehicleIds
);

public class GetEmployeesQueryHandler : IRequestHandler<GetEmployeesQuery, List<EmployeeDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetEmployeesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<EmployeeDto>> Handle(GetEmployeesQuery request, CancellationToken ct)
    {
        // Écran opérationnel : on borne explicitement à la société de l'appelant.
        // Le filtre global multi-tenance est contourné pour les administrateurs
        // système, sans ça la liste des employés fuitait toutes les sociétés.
        var companyId = _tenantService.CompanyId ?? 0;

        var query = _context.Users
            .Include(u => u.Role)
            .Include(u => u.UserVehicles)
            .Where(u => u.CompanyId == companyId && u.EmployeeRole != null);

        if (!string.IsNullOrWhiteSpace(request.Role))
            query = query.Where(u => u.EmployeeRole == request.Role);

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(u => u.Status == request.Status);

        var users = await query.OrderBy(u => u.FirstName).ToListAsync(ct);

        // AssignedVehicleIds publiait la CARTOGRAPHIE COMPLÈTE « quel utilisateur voit
        // quels véhicules ». Chez un LOUEUR, cette carte dit qui loue quoi : un locataire
        // y lisait le périmètre de tous les autres. On ne nomme donc que les véhicules de
        // SA propre portée — null = administrateur, il garde la carte entière.
        // Le reste de la fiche (nom, CIN, permis) n'est pas rattaché à un véhicule et
        // n'est pas touché ici : qui peut lister les employés est une décision métier.
        var portee = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        // Pas de « véhicule affecté » pour un employé. Il était déduit de
        // vehicles.assigned_driver_id == users.id, or cette colonne est une clé
        // étrangère vers drivers(id) : la fiche affichait le véhicule du chauffeur
        // qui porte par hasard le même numéro. Aucun lien employé ↔ chauffeur
        // n'existe depuis le découplage (drivers.user_id n'est ni mappé ni renseigné).
        // Les véhicules visibles par l'employé restent dans AssignedVehicleIds (user_vehicles).
        return users.Select(u =>
        {
            var userVehicleIds = u.UserVehicles
                .Select(uv => uv.VehicleId)
                .Where(id => portee is null || portee.Contains(id))
                .ToArray();

            return new EmployeeDto(
                u.Id,
                u.FirstName,
                u.LastName,
                u.FullName,
                u.Email,
                u.Phone,
                u.EmployeeRole,
                u.Status,
                u.PermitNumber,
                u.PermitType,
                u.PermitExpiry,
                u.CIN,
                u.DateOfBirth,
                u.HireDate,
                null,
                null,
                null,
                userVehicleIds.Length > 0 ? userVehicleIds : null
            );
        }).ToList();
    }
}
