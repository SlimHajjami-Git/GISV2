using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceTokensController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly IFcmService _fcm;

    public DeviceTokensController(IGisDbContext context, IFcmService fcm)
    {
        _context = context;
        _fcm = fcm;
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

    /// <summary>
    /// Diagnostic: send a test push to the CURRENT user's active device(s) and
    /// return the FCM result (initialized? token count? success/failure + error codes).
    /// Call this with the app CLOSED to verify background delivery end-to-end.
    /// </summary>
    [HttpPost("test-push")]
    public async Task<IActionResult> TestPush()
    {
        var userId = GetUserId();
        var result = await _fcm.SendToUserAsync(
            userId,
            "🔔 Test de notification",
            "Si tu vois ceci, les notifications push fonctionnent !",
            new Dictionary<string, string> { ["type"] = "test" },
            badgeCount: 1);
        return Ok(result);
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
