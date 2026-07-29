using GisAPI.Application.Common.Interfaces;
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

        // Load assigned vehicles for mapping
        var vehicleIds = users
            .Where(u => u.UserVehicles.Any())
            .SelectMany(u => u.UserVehicles.Select(uv => uv.VehicleId))
            .Distinct()
            .ToList();

        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId && vehicleIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.Plate, v.AssignedDriverId })
            .ToListAsync(ct);

        return users.Select(u =>
        {
            var assignedVehicle = vehicles.FirstOrDefault(v => v.AssignedDriverId == u.Id);
            var userVehicleIds = u.UserVehicles.Select(uv => uv.VehicleId).ToArray();

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
                assignedVehicle?.Id,
                assignedVehicle?.Name,
                assignedVehicle?.Plate,
                userVehicleIds.Length > 0 ? userVehicleIds : null
            );
        }).ToList();
    }
}
