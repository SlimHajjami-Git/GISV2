using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceTokensController : ControllerBase
{
    private readonly IGisDbContext _context;

    public DeviceTokensController(IGisDbContext context)
    {
        _context = context;
    }

    private int GetUserId() => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterTokenRequest req)
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(req.Token)) return BadRequest(new { message = "Token requis" });

        var existing = await _context.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == req.Token);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.IsActive = true;
            existing.Platform = req.Platform ?? "android";
            existing.LastUsedAt = DateTime.UtcNow;
        }
        else
        {
            _context.UserDeviceTokens.Add(new UserDeviceToken
            {
                UserId = userId,
                Token = req.Token,
                Platform = req.Platform ?? "android",
                IsActive = true,
                RegisteredAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpDelete]
    public async Task<IActionResult> Unregister([FromBody] UnregisterTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Token)) return BadRequest();

        var token = await _context.UserDeviceTokens
            .FirstOrDefaultAsync(t => t.Token == req.Token);

        if (token != null)
        {
            token.IsActive = false;
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }
}

public class RegisterTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string? Platform { get; set; }
}

public class UnregisterTokenRequest
{
    public string Token { get; set; } = string.Empty;
}
