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
    public const string WrongCredentialsMessage = "E-mail ou mot de passe incorrect.";

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

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            throw new DomainException("Saisissez votre e-mail et votre mot de passe.");

        // Filtre société explicite : le filtre global est contourné pour un administrateur système.
        var user = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        // Le jeton emprunte les droits de la session ouverte : l'e-mail ressaisi doit être CELUI de
        // cette session, pas celui d'un autre compte dont on connaîtrait le mot de passe. Le mot de
        // passe est vérifié dans tous les cas, pour que les deux refus coûtent pareil et disent pareil.
        var emailMatches = string.Equals(request.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase);
        var passwordMatches = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!emailMatches || !passwordMatches)
        {
            // Pas d'adresse dans le journal : le champ reçoit parfois un mot de passe tapé au mauvais endroit.
            _logger.LogWarning("Génération de jeton d'API refusée pour l'utilisateur {UserId} depuis {Ip} : identifiants incorrects",
                userId, request.IpAddress ?? "adresse inconnue");
            throw new DomainException(WrongCredentialsMessage);
        }

        var refusal = ApiTokenAccess.AccountRefusal(user,
            await ApiTokenAccess.CompanyApiAccessEnabledAsync(_context, companyId, ct));
        if (refusal != null)
            throw new DomainException(refusal);

        var now = DateTime.UtcNow;
        var pending = await _context.ApiTokens.IgnoreQueryFilters()
            .CountAsync(t => t.UserId == userId && t.UsedAt == null && t.ExpiresAt > now, ct);
        if (pending >= ApiTokenAccess.MaxPendingPerUser)
            throw new DomainException(TooManyPendingMessage);

        var token = ApiTokenAccess.Generate();
        var entity = new ApiToken
        {
            CompanyId = companyId,
            UserId = userId,
            TokenPrefix = ApiTokenAccess.DisplayPrefix(token),
            TokenHash = ApiTokenAccess.Hash(token),
            ExpiresAt = now.AddMinutes(ApiTokenAccess.LifetimeMinutes),
            CreatedIp = request.IpAddress?.Length > 64 ? request.IpAddress[..64] : request.IpAddress,
            CreatedAt = now,
            UpdatedAt = now
        };
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
