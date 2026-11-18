using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.ApiTokens.Commands.GenerateApiToken;

public class GenerateApiTokenCommandHandler : IRequestHandler<GenerateApiTokenCommand, ApiTokenCreatedDto>
{
    /// <summary>Même message pour un e-mail qui n'est pas le sien et pour un mauvais mot de passe.</summary>
    public const string WrongCredentialsMessage = ReenteredCredentials.WrongCredentialsMessage;

    public static readonly string TooManyPendingMessage =
        $"Vous avez déjà {ApiTokenAccess.MaxPendingPerUser} jetons en attente. "
        + "Utilisez-en un, ou attendez leur expiration.";

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<GenerateApiTokenCommandHandler> _logger;

    public GenerateApiTokenCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenant,
        IPasswordHasher passwordHasher,
        ILogger<GenerateApiTokenCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ApiTokenCreatedDto> Handle(GenerateApiTokenCommand request, CancellationToken ct)
    {
        var userId = _tenant.UserId ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenant.CompanyId ?? throw new DomainException("Société non identifiée");

        // Le jeton emprunte les droits de la session ouverte : l'e-mail ressaisi doit être CELUI de
        // cette session (règle partagée avec la création d'une clé d'automatisation, migration 069).
        var user = await ReenteredCredentials.VerifyAsync(_context, _passwordHasher, _logger,
            userId, companyId, request.Email, request.Password, request.IpAddress,
            "Génération de jeton d'API", ct);

        var refusal = ApiTokenAccess.AccountRefusal(user,
            await ApiTokenAccess.CompanyApiAccessEnabledAsync(_context, companyId, ct));
        if (refusal != null)
            throw new DomainException(refusal);

        var now = DateTime.UtcNow;
        var pending = await _context.ApiTokens.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now, ct);
        if (pending >= ApiTokenAccess.MaxPendingPerUser)
            throw new DomainException(TooManyPendingMessage);

        var (token, entity) = ApiTokenAccess.NewToken(companyId, userId, request.IpAddress, now);
        _context.ApiTokens.Add(entity);

        // Trace durable de la génération (qui, quand, d'où), sans le jeton. Écrite avec le jeton :
        // l'un n'existe pas sans l'autre.
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            CompanyId = companyId,
            Action = "api_token_generated",
            EntityType = "ApiToken",
            EntityName = entity.TokenPrefix,
            Description = "Jeton d'API à usage unique généré",
            IpAddress = request.IpAddress,
            Timestamp = now
        });
        await _context.SaveChangesAsync(ct);

        return new ApiTokenCreatedDto(
            entity.Id, token, entity.TokenPrefix, entity.ExpiresAt, ApiTokenAccess.LifetimeMinutes * 60);
    }
}
