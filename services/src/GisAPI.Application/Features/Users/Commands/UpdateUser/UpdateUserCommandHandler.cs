using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.UpdateUser;

public class UpdateUserCommandHandler : IRequestHandler<UpdateUserCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateUserCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UpdateUserCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == request.Id && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        // Auto-select role based on permissions (same logic as CreateUser)
        var allPerms = (request.CanMonitoring ?? user.CanMonitoring)
            && (request.CanVehicles ?? user.CanVehicles) && (request.CanDrivers ?? user.CanDrivers)
            && (request.CanReports ?? user.CanReports) && (request.CanGeofences ?? user.CanGeofences)
            && (request.CanMaintenance ?? user.CanMaintenance) && (request.CanCosts ?? user.CanCosts)
            && (request.CanFuel ?? user.CanFuel)
            && (request.CanDocuments ?? user.CanDocuments) && (request.CanAccidents ?? user.CanAccidents)
            && (request.CanUsers ?? user.CanUsers) && (request.CanSettings ?? user.CanSettings)
            && (request.CanSuppliers ?? user.CanSuppliers) && (request.CanFleetManagement ?? user.CanFleetManagement)
            && (request.CanTours ?? user.CanTours) && (request.CanPlayback ?? user.CanPlayback);

        if (allPerms)
        {
            var adminRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.SocieteId == companyId && r.IsCompanyAdmin, ct);
            if (adminRole != null) user.RoleId = adminRole.Id;
        }
        else
        {
            var nonAdminRole = await _context.Roles
                .FirstOrDefaultAsync(r => r.SocieteId == companyId && !r.IsCompanyAdmin, ct);
            if (nonAdminRole == null)
            {
                nonAdminRole = new Role { Name = "Opérateur", SocieteId = companyId, IsCompanyAdmin = false, IsSystemRole = false };
                _context.Roles.Add(nonAdminRole);
                await _context.SaveChangesAsync(ct);
            }
            user.RoleId = nonAdminRole.Id;
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Email = request.Email;
        user.Phone = request.Phone;
        if (!string.IsNullOrEmpty(request.Status))
            user.Status = request.Status;
        user.EmployeeRole = request.EmployeeRole;
        user.PermitNumber = request.PermitNumber;
        user.PermitType = request.PermitType;
        user.PermitExpiry = request.PermitExpiry;
        user.CIN = request.CIN;
        user.DateOfBirth = request.DateOfBirth;
        user.HireDate = request.HireDate;
        user.UpdatedAt = DateTime.UtcNow;

        // Update permissions if provided
        if (request.AccessLevel != null) user.AccessLevel = request.AccessLevel;
        if (request.CanMonitoring.HasValue) user.CanMonitoring = request.CanMonitoring.Value;
        if (request.CanVehicles.HasValue) user.CanVehicles = request.CanVehicles.Value;
        if (request.CanDrivers.HasValue) user.CanDrivers = request.CanDrivers.Value;
        if (request.CanReports.HasValue) user.CanReports = request.CanReports.Value;
        if (request.CanGeofences.HasValue) user.CanGeofences = request.CanGeofences.Value;
        if (request.CanMaintenance.HasValue) user.CanMaintenance = request.CanMaintenance.Value;
        if (request.CanCosts.HasValue) user.CanCosts = request.CanCosts.Value;
        if (request.CanFuel.HasValue) user.CanFuel = request.CanFuel.Value;
        if (request.CanDocuments.HasValue) user.CanDocuments = request.CanDocuments.Value;
        if (request.CanAccidents.HasValue) user.CanAccidents = request.CanAccidents.Value;
        if (request.CanUsers.HasValue) user.CanUsers = request.CanUsers.Value;
        if (request.CanSettings.HasValue) user.CanSettings = request.CanSettings.Value;
        if (request.CanSuppliers.HasValue) user.CanSuppliers = request.CanSuppliers.Value;
        if (request.CanFleetManagement.HasValue) user.CanFleetManagement = request.CanFleetManagement.Value;
        if (request.CanTours.HasValue) user.CanTours = request.CanTours.Value;
        if (request.CanPlayback.HasValue) user.CanPlayback = request.CanPlayback.Value;
        // Per-report permissions
        if (request.CanReportTrips.HasValue) user.CanReportTrips = request.CanReportTrips.Value;
        if (request.CanReportFuel.HasValue) user.CanReportFuel = request.CanReportFuel.Value;
        if (request.CanReportSpeed.HasValue) user.CanReportSpeed = request.CanReportSpeed.Value;
        if (request.CanReportStops.HasValue) user.CanReportStops = request.CanReportStops.Value;
        if (request.CanReportMileage.HasValue) user.CanReportMileage = request.CanReportMileage.Value;
        if (request.CanReportCosts.HasValue) user.CanReportCosts = request.CanReportCosts.Value;
        if (request.CanReportMaintenance.HasValue) user.CanReportMaintenance = request.CanReportMaintenance.Value;
        if (request.CanReportDaily.HasValue) user.CanReportDaily = request.CanReportDaily.Value;
        if (request.CanReportMonthly.HasValue) user.CanReportMonthly = request.CanReportMonthly.Value;
        if (request.CanReportMileagePeriod.HasValue) user.CanReportMileagePeriod = request.CanReportMileagePeriod.Value;
        if (request.CanReportSpeedInfraction.HasValue) user.CanReportSpeedInfraction = request.CanReportSpeedInfraction.Value;
        if (request.CanReportDrivingBehavior.HasValue) user.CanReportDrivingBehavior = request.CanReportDrivingBehavior.Value;

        // Update vehicle assignments if provided
        if (request.AssignedVehicleIds != null)
        {
            // Validate vehicles belong to company
            if (request.AssignedVehicleIds.Length > 0)
            {
                var validCount = await _context.Vehicles
                    .CountAsync(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == companyId, ct);
                if (validCount != request.AssignedVehicleIds.Length)
                    throw new DomainException("Un ou plusieurs véhicules sont invalides");
            }

            // Remove old assignments
            var oldAssignments = await _context.UserVehicles
                .Where(uv => uv.UserId == request.Id)
                .ToListAsync(ct);
            _context.UserVehicles.RemoveRange(oldAssignments);

            // Add new assignments
            foreach (var vehicleId in request.AssignedVehicleIds)
            {
                _context.UserVehicles.Add(new UserVehicle
                {
                    UserId = request.Id,
                    VehicleId = vehicleId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedById = currentUserId
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }
}
