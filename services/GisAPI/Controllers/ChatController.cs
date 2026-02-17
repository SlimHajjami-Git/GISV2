using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly IHubContext<GpsHub> _hubContext;

    public ChatController(IGisDbContext context, IHubContext<GpsHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Get list of users available for chat (same company, excluding self)
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> GetChatUsers()
    {
        var userId = GetUserId();

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id != userId && u.Status == "active")
            .Select(u => new
            {
                u.Id,
                Name = (u.FirstName + " " + u.LastName).Trim(),
                u.Email,
                u.Status,
                u.LastLoginAt
            })
            .OrderBy(u => u.Name)
            .ToListAsync();

        // Get unread counts per sender
        var unreadCounts = await _context.ChatMessages
            .Where(m => m.ReceiverId == userId && !m.IsRead)
            .GroupBy(m => m.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .ToListAsync();

        var unreadMap = unreadCounts.ToDictionary(x => x.SenderId, x => x.Count);

        var result = users.Select(u => new
        {
            u.Id,
            u.Name,
            u.Email,
            IsOnline = u.LastLoginAt.HasValue && u.LastLoginAt.Value > DateTime.UtcNow.AddMinutes(-5),
            LastSeen = u.LastLoginAt,
            UnreadCount = unreadMap.GetValueOrDefault(u.Id, 0)
        });

        return Ok(result);
    }

    /// <summary>
    /// Get conversation messages between current user and another user
    /// </summary>
    [HttpGet("messages/{otherUserId}")]
    public async Task<IActionResult> GetMessages(int otherUserId, [FromQuery] int limit = 50, [FromQuery] int? beforeId = null)
    {
        var userId = GetUserId();

        var query = _context.ChatMessages
            .AsNoTracking()
            .Where(m =>
                (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                (m.SenderId == otherUserId && m.ReceiverId == userId));

        if (beforeId.HasValue)
        {
            query = query.Where(m => m.Id < beforeId.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.SenderId,
                SenderName = m.Sender.FirstName + " " + m.Sender.LastName,
                m.ReceiverId,
                m.Content,
                Timestamp = m.CreatedAt,
                m.IsRead,
                IsMine = m.SenderId == userId
            })
            .ToListAsync();

        // Return in chronological order
        messages.Reverse();
        return Ok(messages);
    }

    /// <summary>
    /// Send a message to another user
    /// </summary>
    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Le contenu du message ne peut pas être vide");

        if (request.ReceiverId == userId)
            return BadRequest("Vous ne pouvez pas vous envoyer un message à vous-même");

        // Verify receiver exists in same company
        var receiver = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.ReceiverId);

        if (receiver == null)
            return NotFound("Destinataire introuvable");

        var sender = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        var message = new ChatMessage
        {
            SenderId = userId,
            ReceiverId = request.ReceiverId,
            Content = request.Content.Trim()
        };

        _context.ChatMessages.Add(message);
        await _context.SaveChangesAsync();

        var senderName = sender != null ? $"{sender.FirstName} {sender.LastName}".Trim() : "Utilisateur";

        var messageDto = new
        {
            message.Id,
            message.SenderId,
            SenderName = senderName,
            message.ReceiverId,
            message.Content,
            Timestamp = message.CreatedAt,
            message.IsRead,
            IsMine = true
        };

        // Send real-time notification to receiver via SignalR
        await _hubContext.Clients.Group($"user_{request.ReceiverId}")
            .SendAsync("ChatMessage", new
            {
                message.Id,
                message.SenderId,
                SenderName = senderName,
                message.ReceiverId,
                message.Content,
                Timestamp = message.CreatedAt,
                message.IsRead,
                IsMine = false
            });

        return Ok(messageDto);
    }

    /// <summary>
    /// Mark all messages from a specific user as read
    /// </summary>
    [HttpPut("messages/{otherUserId}/read")]
    public async Task<IActionResult> MarkAsRead(int otherUserId)
    {
        var userId = GetUserId();

        var unreadMessages = await _context.ChatMessages
            .Where(m => m.SenderId == otherUserId && m.ReceiverId == userId && !m.IsRead)
            .ToListAsync();

        if (unreadMessages.Count > 0)
        {
            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            // Notify sender that messages were read
            await _hubContext.Clients.Group($"user_{otherUserId}")
                .SendAsync("ChatMessagesRead", new { ReadBy = userId, Count = unreadMessages.Count });
        }

        return Ok(new { MarkedRead = unreadMessages.Count });
    }

    /// <summary>
    /// Get total unread message count for current user
    /// </summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetUserId();

        var count = await _context.ChatMessages
            .CountAsync(m => m.ReceiverId == userId && !m.IsRead);

        return Ok(new { Count = count });
    }
}

public record SendMessageRequest(int ReceiverId, string Content);
