using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Queries;

public record GetDriversQuery() : IRequest<List<DriverDto>>;

public class GetDriversQueryHandler : IRequestHandler<GetDriversQuery, List<DriverDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDriversQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<DriverDto>> Handle(GetDriversQuery request, CancellationToken ct)
    {
        // Écran OPÉRATIONNEL : on borne toujours à la société de l'appelant, y
        // compris pour un administrateur système. Le filtre global de
        // multi-tenance (GisDbContext) est volontairement contourné pour les
        // rôles système — sans ce filtre explicite, la page « Chauffeurs »
        // affichait donc les chauffeurs de TOUTES les sociétés. Le service de
        // tenant était déjà injecté ici mais n'était pas utilisé.
        // Même correctif que GetExpiriesQueryHandler.
        var companyId = _tenantService.CompanyId ?? 0;

        // La fiche chauffeur nomme le VÉHICULE affecté (nom + plaque). Chez un loueur,
        // cela revient à dire quel chauffeur conduit le véhicule loué à quel client : on
        // ne nomme donc que les véhicules de la portée de l'appelant. TROIS états :
        // null = administrateur, rien ne change ; liste non vide = ses véhicules ;
        // liste VIDE = aucun véhicule nommé. La LISTE des chauffeurs elle-même n'est pas
        // rattachée à un véhicule : qui peut la lire est une décision métier, non tranchée ici.
        var portee = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        var tousVehicules = portee is null;
        List<int> vehiculesVisibles = portee ?? new List<int>();

        // TRADUCTION VÉRIFIÉE LE 23/09/2026 (EF Core 9.0.1 / Npgsql 9.0.3) : le
        // « Contains » ci-dessous est dans la PROJECTION, pas dans un Where, et les
        // tests tournent sur SQLite — un autre fournisseur ne prouve pas que PostgreSQL
        // traduit. Contrôle fait hors base, par ToQueryString sur un contexte Npgsql
        // jamais ouvert : les deux branches passent entièrement en SQL, aucune
        // évaluation côté client, aucun « could not be translated ».
        //   • restreint : « CASE WHEN v.id IS NOT NULL AND v.id = ANY(@vehiculesVisibles)
        //     THEN … END » — la portée devient UN paramètre tableau ;
        //   • administrateur : EF replie le booléen capturé, le test disparaît et la
        //     colonne est rendue telle quelle.
        // Deux formes SQL distinctes, donc deux entrées de cache distinctes : un
        // administrateur ne peut pas hériter du plan compilé d'un locataire. À refaire
        // avant toute montée de version d'EF ou de Npgsql : la suite de tests, elle, ne
        // verra jamais cette régression.
        //
        // Driver is a standalone entity with native fields — no Include on User.
        // Driver.AssignedVehicle is NOT a navigation (see Driver.cs) because it
        // would collide with Vehicle.AssignedDriver in EF's relationship discovery.
        // We join on AssignedVehicleId manually to project vehicle name/plate.
        return await (from d in _context.Drivers
                      where d.CompanyId == companyId
                      join v in _context.Vehicles
                          on d.AssignedVehicleId equals v.Id into vehicleJoin
                      from vehicle in vehicleJoin.DefaultIfEmpty()
                      // Compte chauffeur relié (migration 050), pour le badge « application ».
                      join u in _context.Users
                          on d.UserId equals u.Id into userJoin
                      from compte in userJoin.DefaultIfEmpty()
                      orderby d.LastName, d.FirstName
                      select new DriverDto(
                          d.Id,
                          d.FirstName,
                          d.LastName,
                          d.Email,
                          d.Phone,
                          d.PermitNumber,
                          d.PermitType,
                          d.PermitExpiry,
                          d.CIN,
                          d.DateOfBirth,
                          d.HireDate,
                          vehicle != null && (tousVehicules || vehiculesVisibles.Contains(vehicle.Id))
                              ? d.AssignedVehicleId : null,
                          vehicle != null && (tousVehicules || vehiculesVisibles.Contains(vehicle.Id))
                              ? vehicle.Name : null,
                          vehicle != null && (tousVehicules || vehiculesVisibles.Contains(vehicle.Id))
                              ? vehicle.Plate : null,
                          d.Status,
                          d.CreatedAt,
                          d.UserId,
                          compte != null ? compte.Status : null
                      ))
                      .ToListAsync(ct);
    }
}
