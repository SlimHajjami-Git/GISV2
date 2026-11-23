using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.ApiKeys.Commands.RevokeApiKey;

/// <summary>Révoque une clé d'automatisation de la société. Rejouer sur une clé révoquée est sans effet.</summary>
public record RevokeApiKeyCommand(int Id, string? IpAddress = null) : ICommand<bool>;

/// <summary>
/// Révocation par l'administrateur de la société (pas seulement par le créateur : il peut avoir
/// quitté la société). La ligne est conservée pour l'historique ; la clé ne s'échange plus jamais.
/// Les jetons déjà obtenus vivent leurs 15 minutes au plus.
/// </summary>
public class RevokeApiKeyCommandHandler : IRequestHandler<RevokeApiKeyCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public RevokeApiKeyCommandHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<bool> Handle(RevokeApiKeyCommand request, CancellationToken ct)
    {
        var (user, companyId) = await ApiKeyAdmin.RequireAsync(_context, _tenant, ct);

        // Filtre société explicite : la clé d'une autre société est « introuvable », pas « interdite ».
        var key = await _context.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Id == request.Id && k.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Clé introuvable.");

        if (key.RevokedAt != null)
            return true;

        var now = DateTime.UtcNow;
        key.RevokedAt = now;
        key.RevokedByUserId = user.Id;
        key.UpdatedAt = now;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            CompanyId = companyId,
            Action = "api_key_revoked",
            EntityType = "ApiKey",
            EntityId = key.Id,
            EntityName = key.KeyPrefix,
            Description = $"Clé d'API révoquée : {key.Label}",
            IpAddress = request.IpAddress,
            Timestamp = now
        });
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
