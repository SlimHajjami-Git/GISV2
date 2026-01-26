using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = "super_admin,platform_admin")]
public class AdminUserController : ControllerBase
{
    private readonly GisDbContext _context;

    public AdminUserController(GisDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<List<AdminUserDto>>> GetAllUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? companyId = null)
    {
        var query = _context.Users
            .Include(u => u.Societe)
            .AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(u =>
                u.Name.ToLower().Contains(search.ToLower()) ||
                u.Email.ToLower().Contains(search.ToLower()) ||
                (u.Societe != null && u.Societe.Name.ToLower().Contains(search.ToLower())));
        }

        if (!string.IsNullOrEmpty(status) && status != "all")
        {
            query = query.Where(u => u.Status == status);
        }

        if (companyId.HasValue)
        {
            query = query.Where(u => u.CompanyId == companyId.Value);
        }

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Phone = u.Phone,
                DateOfBirth = u.DateOfBirth,
                CIN = u.CIN,
                CompanyId = u.CompanyId,
                CompanyName = u.Societe != null ? u.Societe.Name : null,
                RoleId = u.RoleId,
                RoleName = u.Role != null ? u.Role.Name : null,
                Roles = u.Roles,
                Permissions = u.Permissions,
                AssignedVehicleIds = u.AssignedVehicleIds,
                Status = u.Status,
                LastLoginAt = u.LastLoginAt,
                CreatedAt = u.CreatedAt,
                IsOnline = u.LastLoginAt.HasValue && u.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-15)
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AdminUserDto>> GetUser(int id)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            DateOfBirth = user.DateOfBirth,
            CIN = user.CIN,
            CompanyId = user.CompanyId,
            CompanyName = user.Societe?.Name,
            RoleId = user.RoleId,
            RoleName = user.Role?.Name,
            Roles = user.Roles,
            Permissions = user.Permissions,
            AssignedVehicleIds = user.AssignedVehicleIds,
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            IsOnline = user.LastLoginAt.HasValue && user.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-15)
        });
    }

    [HttpPost]
    public async Task<ActionResult<AdminUserDto>> CreateUser([FromBody] CreateAdminUserRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { message = "Un utilisateur avec cet email existe déjà" });
        }

        var company = await _context.Societes.FindAsync(request.CompanyId);
        if (company == null)
        {
            return BadRequest(new { message = "Société non trouvée" });
        }

        if (!company.IsActive)
        {
            return BadRequest(new { message = "Impossible de créer un utilisateur pour une société suspendue" });
        }

        // Validate RoleId if provided
        Role? role = null;
        if (request.RoleId.HasValue)
        {
            role = await _context.Roles.FindAsync(request.RoleId.Value);
            if (role == null || (role.SocieteId.HasValue && role.SocieteId != request.CompanyId))
            {
                return BadRequest(new { message = "Rôle non trouvé ou non accessible" });
            }
        }

        // Validate AssignedVehicleIds if provided
        if (request.AssignedVehicleIds != null && request.AssignedVehicleIds.Length > 0)
        {
            var vehicleCount = await _context.Vehicles
                .Where(v => request.AssignedVehicleIds.Contains(v.Id) && v.CompanyId == request.CompanyId)
                .CountAsync();
            if (vehicleCount != request.AssignedVehicleIds.Length)
            {
                return BadRequest(new { message = "Un ou plusieurs véhicules sont invalides ou n'appartiennent pas à cette société" });
            }
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            DateOfBirth = request.DateOfBirth,
            CIN = request.CIN,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = request.RoleId,
            AssignedVehicleIds = request.AssignedVehicleIds ?? [],
            Roles = request.Roles ?? new[] { "user" },
            Permissions = request.Permissions ?? new[] { "dashboard", "monitoring" },
            CompanyId = request.CompanyId,
            Status = "active"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, new AdminUserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            DateOfBirth = user.DateOfBirth,
            CIN = user.CIN,
            CompanyId = user.CompanyId,
            CompanyName = company.Name,
            RoleId = user.RoleId,
            RoleName = role?.Name,
            Roles = user.Roles,
            Permissions = user.Permissions,
            AssignedVehicleIds = user.AssignedVehicleIds,
            Status = user.Status,
            CreatedAt = user.CreatedAt,
            IsOnline = false
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AdminUserDto>> UpdateUser(int id, [FromBody] UpdateAdminUserRequest request)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
        {
            if (await _context.Users.AnyAsync(u => u.Email == request.Email && u.Id != id))
            {
                return BadRequest(new { message = "Un utilisateur avec cet email existe déjà" });
            }
            user.Email = request.Email;
        }

        if (!string.IsNullOrEmpty(request.Name)) user.Name = request.Name;
        if (request.Phone != null) user.Phone = request.Phone;
        if (request.Roles != null && request.Roles.Length > 0) user.Roles = request.Roles;
        if (request.Permissions != null) user.Permissions = request.Permissions;
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new AdminUserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            CompanyId = user.CompanyId,
            CompanyName = user.Societe?.Name,
            Roles = user.Roles,
            Permissions = user.Permissions,
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            IsOnline = user.LastLoginAt.HasValue && user.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-15)
        });
    }

    [HttpPut("{id}/permissions")]
    public async Task<ActionResult> UpdateUserPermissions(int id, [FromBody] UpdatePermissionsRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.Permissions = request.Permissions;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Permissions mises à jour" });
    }

    [HttpPut("{id}/roles")]
    public async Task<ActionResult> UpdateUserRoles(int id, [FromBody] UpdateRolesRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.Roles = request.Roles;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Rôles mis à jour" });
    }

    [HttpPost("{id}/suspend")]
    public async Task<ActionResult> SuspendUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.Status = "suspended";
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Utilisateur suspendu" });
    }

    [HttpPost("{id}/activate")]
    public async Task<ActionResult> ActivateUser(int id)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        if (user.Societe != null && !user.Societe.IsActive)
        {
            return BadRequest(new { message = "Impossible d'activer un utilisateur d'une société suspendue" });
        }

        user.Status = "active";
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Utilisateur activé" });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Utilisateur supprimé" });
    }

    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult> ResetUserPassword(int id, [FromBody] ResetPasswordRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Mot de passe réinitialisé" });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<UserStatsDto>> GetUserStats()
    {
        var totalUsers = await _context.Users.CountAsync();
        var activeUsers = await _context.Users.CountAsync(u => u.Status == "active");
        var suspendedUsers = await _context.Users.CountAsync(u => u.Status == "suspended");
        var onlineThreshold = DateTime.UtcNow.AddMinutes(-15);
        var onlineUsers = await _context.Users.CountAsync(u => u.LastLoginAt != null && u.LastLoginAt > onlineThreshold);

        var usersByCompany = await _context.Users
            .Include(u => u.Societe)
            .GroupBy(u => new { u.CompanyId, CompanyName = u.Societe != null ? u.Societe.Name : "Unknown" })
            .Select(g => new CompanyUserCount
            {
                CompanyId = g.Key.CompanyId,
                CompanyName = g.Key.CompanyName,
                UserCount = g.Count()
            })
            .OrderByDescending(x => x.UserCount)
            .Take(10)
            .ToListAsync();

        return Ok(new UserStatsDto
        {
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            SuspendedUsers = suspendedUsers,
            OnlineUsers = onlineUsers,
            UsersByCompany = usersByCompany
        });
    }

    [HttpGet("available-permissions")]
    public ActionResult<List<PermissionInfo>> GetAvailablePermissions()
    {
        var permissions = new List<PermissionInfo>
        {
            new() { Key = "dashboard", Label = "Tableau de bord", Description = "Accès au tableau de bord principal" },
            new() { Key = "monitoring", Label = "Monitoring", Description = "Suivi en temps réel des véhicules" },
            new() { Key = "vehicles", Label = "Véhicules", Description = "Gestion des véhicules" },
            new() { Key = "employees", Label = "Employés", Description = "Gestion des employés/conducteurs" },
            new() { Key = "gps-devices", Label = "Appareils GPS", Description = "Gestion des appareils GPS" },
            new() { Key = "maintenance", Label = "Maintenance", Description = "Gestion de la maintenance" },
            new() { Key = "costs", Label = "Coûts", Description = "Suivi des coûts" },
            new() { Key = "reports", Label = "Rapports", Description = "Génération de rapports" },
            new() { Key = "geofences", Label = "Géofences", Description = "Gestion des zones géographiques" },
            new() { Key = "notifications", Label = "Notifications", Description = "Gestion des notifications" },
            new() { Key = "settings", Label = "Paramètres", Description = "Configuration du compte" },
            new() { Key = "users", Label = "Utilisateurs", Description = "Gestion des utilisateurs de la société" }
        };

        return Ok(permissions);
    }

    [HttpGet("available-roles")]
    public ActionResult<List<RoleInfo>> GetAvailableRoles()
    {
        var roles = new List<RoleInfo>
        {
            new() { Key = "admin", Label = "Administrateur", Description = "Accès complet à toutes les fonctionnalités" },
            new() { Key = "manager", Label = "Gestionnaire", Description = "Gestion des véhicules et employés" },
            new() { Key = "supervisor", Label = "Superviseur", Description = "Supervision et rapports" },
            new() { Key = "operator", Label = "Opérateur", Description = "Monitoring et suivi basique" },
            new() { Key = "user", Label = "Utilisateur", Description = "Accès en lecture seule" }
        };

        return Ok(roles);
    }
}

// DTOs
public class AdminUserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? CIN { get; set; }
    public int CompanyId { get; set; }
    public string? CompanyName { get; set; }
    public int? RoleId { get; set; }
    public string? RoleName { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
    public int[] AssignedVehicleIds { get; set; } = [];
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsOnline { get; set; }
}

public class CreateAdminUserRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? CIN { get; set; }
    public int CompanyId { get; set; }
    public int? RoleId { get; set; }
    public int[]? AssignedVehicleIds { get; set; }
    public string[]? Roles { get; set; }
    public string[]? Permissions { get; set; }
}

public class UpdateAdminUserRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Password { get; set; }
    public string[]? Roles { get; set; }
    public string[]? Permissions { get; set; }
}

public class UpdatePermissionsRequest
{
    public string[] Permissions { get; set; } = [];
}

public class UpdateRolesRequest
{
    public string[] Roles { get; set; } = [];
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = string.Empty;
}

public class UserStatsDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedUsers { get; set; }
    public int OnlineUsers { get; set; }
    public List<CompanyUserCount> UsersByCompany { get; set; } = new();
}

public class CompanyUserCount
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int UserCount { get; set; }
}

public class PermissionInfo
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class RoleInfo
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
