using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ILogger<LogoutCommandHandler> _logger;

    public LogoutCommandHandler(IGisDbContext context, ILogger<LogoutCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        // Best-effort: record the logout + revoke active refresh tokens. Never throw on logout.
        try
        {
            var activeTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == request.UserId && rt.RevokedAt == null)
                .ToListAsync(ct);
            foreach (var t in activeTokens)
                t.RevokedAt = DateTime.UtcNow;

            _context.AuditLogs.Add(new GisAPI.Domain.Entities.AuditLog
            {
                UserId = request.UserId,
                CompanyId = request.CompanyId,
                Action = "logout",
                EntityType = "User",
                EntityId = request.UserId,
                Description = "Déconnexion utilisateur",
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record logout for user {UserId}", request.UserId);
        }

        return Unit.Value;
    }
}
