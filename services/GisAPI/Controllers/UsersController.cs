using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Data;
using GisAPI.DTOs;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly GisDbContext _context;

    public UsersController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<UserListDto>>> GetUsers()
    {
        var companyId = GetCompanyId();

        var users = await _context.Users
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.Name)
            .Select(u => new UserListDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Roles,
                u.Permissions,
                u.AssignedVehicleIds,
                u.Status,
                u.CreatedAt,
                u.LastLoginAt
            ))
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserListDto>> GetUser(int id)
    {
        var companyId = GetCompanyId();

        var user = await _context.Users
            .Where(u => u.Id == id && u.CompanyId == companyId)
            .Select(u => new UserListDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Roles,
                u.Permissions,
                u.AssignedVehicleIds,
                u.Status,
                u.CreatedAt,
                u.LastLoginAt
            ))
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        return Ok(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserListDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        var companyId = GetCompanyId();

        if (await _context.Users.AnyAsync(u => u.Email == request.Email))
        {
            return BadRequest(new { message = "Cet email est déjà utilisé" });
        }

        var user = new User
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Roles = request.Roles,
            Permissions = request.Permissions,
            AssignedVehicleIds = request.AssignedVehicleIds,
            CompanyId = companyId,
            Status = "active"
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, new UserListDto(
            user.Id,
            user.Name,
            user.Email,
            user.Phone,
            user.Roles,
            user.Permissions,
            user.AssignedVehicleIds,
            user.Status,
            user.CreatedAt,
            user.LastLoginAt
        ));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        var companyId = GetCompanyId();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);

        if (user == null)
            return NotFound();

        user.Name = request.Name;
        user.Email = request.Email;
        user.Phone = request.Phone;
        user.Roles = request.Roles;
        user.Permissions = request.Permissions;
        user.AssignedVehicleIds = request.AssignedVehicleIds;
        user.Status = request.Status;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var companyId = GetCompanyId();
        var currentUserId = GetUserId();

        if (id == currentUserId)
        {
            return BadRequest(new { message = "Vous ne pouvez pas supprimer votre propre compte" });
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);

        if (user == null)
            return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserListDto>> GetCurrentUser()
    {
        var userId = GetUserId();

        var user = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserListDto(
                u.Id,
                u.Name,
                u.Email,
                u.Phone,
                u.Roles,
                u.Permissions,
                u.AssignedVehicleIds,
                u.Status,
                u.CreatedAt,
                u.LastLoginAt
            ))
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        return Ok(user);
    }
}
