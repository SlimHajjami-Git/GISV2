using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.ApiKeys.Commands.CreateApiKey;

/// <summary>
/// Crée une clé d'automatisation (migration 069) après ressaisie de l'e-mail et du mot de passe,
/// comme pour un jeton : mêmes conditions (administrateur actif d'une société dont Belive a activé
/// l'accès API). Au plus <see cref="ApiKeyAccess.MaxActivePerCompany"/> clés actives par société.
/// </summary>
public class CreateApiKeyCommandHandler : IRequestHandler<CreateApiKeyCommand, ApiKeyCreatedDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateApiKeyCommandHandler> _logger;

    public CreateApiKeyCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenant,
        IPasswordHasher passwordHasher,
        ILogger<CreateApiKeyCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ApiKeyCreatedDto> Handle(CreateApiKeyCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société non identifiée");

        var user = await ReenteredCredentials.VerifyAsync(_context, _passwordHasher, _logger,
            userId, companyId, request.Email, request.Password, request.IpAddress,
            "Création de clé d'API", ct);

        var refusal = ApiTokenAccess.AccountRefusal(user,
            await ApiTokenAccess.CompanyApiAccessEnabledAsync(_context, companyId, ct));
        if (refusal != null)
            throw new DomainException(refusal);

        var label = string.IsNullOrWhiteSpace(request.Label) ? ApiKeyAccess.DefaultLabel : request.Label.Trim();
        if (label.Length > ApiKeyAccess.LabelMaxLength)
            throw new DomainException(ApiKeyAccess.LabelTooLongMessage);

        var now = DateTime.UtcNow;
        var active = await _context.ApiKeys.IgnoreQueryFilters()
            .CountAsync(k => k.CompanyId == companyId && k.RevokedAt == null && k.ExpiresAt > now, ct);
        if (active >= ApiKeyAccess.MaxActivePerCompany)
            throw new DomainException(ApiKeyAccess.TooManyActiveMessage);

        var key = ApiKeyAccess.Generate();
        var entity = new ApiKey
        {
            CompanyId = companyId,
            UserId = userId,
            Label = label,
            KeyPrefix = ApiKeyAccess.DisplayPrefix(key),
            KeyHash = ApiKeyAccess.Hash(key),
            ExpiresAt = now.AddDays(ApiKeyAccess.LifetimeDays),
            CreatedIp = ApiTokenAccess.TruncateIp(request.IpAddress),
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.ApiKeys.Add(entity);

        // Trace durable de la création (qui, quand, d'où), sans la clé.
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            CompanyId = companyId,
            Action = "api_key_created",
            EntityType = "ApiKey",
            EntityName = entity.KeyPrefix,
            Description = $"Clé d'API créée : {label}",
            IpAddress = request.IpAddress,
            Timestamp = now
        });
        await _context.SaveChangesAsync(ct);

        return new ApiKeyCreatedDto(entity.Id, key, entity.KeyPrefix, entity.Label, entity.ExpiresAt);
    }
}
