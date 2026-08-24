using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.ResetPassword;

/// <summary>
/// Consomme un jeton de réinitialisation et pose le nouveau mot de passe.
/// </summary>
public record ResetPasswordWithTokenCommand(string Token, string NewPassword)
    : IRequest<ResetPasswordWithTokenResult>;

public record ResetPasswordWithTokenResult(bool Success, string Message);

/// <summary>
/// Point d'entrée de changement de mot de passe par jeton.
///
/// <para>Trois garde-fous. Le jeton est à <b>usage unique</b> : il est effacé
/// dès qu'il a servi, pour qu'un lien retrouvé dans une boîte mail des mois
/// plus tard ne rouvre pas le compte. Il est vérifié <b>en date</b> et pas
/// seulement en présence, sans quoi un jeton expiré mais encore stocké
/// resterait valable. Et toutes les <b>sessions ouvertes sont coupées</b> —
/// c'est le point le plus important : si le mot de passe a été perdu parce
/// qu'un tiers y avait accès, laisser vivre les jetons de rafraîchissement
/// existants reviendrait à changer la serrure en laissant les doubles en
/// circulation.</para>
/// </summary>
public class ResetPasswordWithTokenCommandHandler
    : IRequestHandler<ResetPasswordWithTokenCommand, ResetPasswordWithTokenResult>
{
    /// <summary>Longueur exigée, alignée sur celle de l'inscription.</summary>
    private const int MinPasswordLength = 10;

    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ResetPasswordWithTokenCommandHandler> _logger;

    public ResetPasswordWithTokenCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<ResetPasswordWithTokenCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<ResetPasswordWithTokenResult> Handle(
        ResetPasswordWithTokenCommand request, CancellationToken ct)
    {
        var token = (request.Token ?? string.Empty).Trim();
        var password = request.NewPassword ?? string.Empty;

        if (token.Length == 0)
            return new ResetPasswordWithTokenResult(false, "Lien invalide.");

        if (password.Length < MinPasswordLength)
            return new ResetPasswordWithTokenResult(false,
                $"Le mot de passe doit compter au moins {MinPasswordLength} caractères.");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == token, ct);

        // Un jeton inconnu et un jeton périmé rendent le MÊME message : distinguer
        // les deux apprendrait à un attaquant que le jeton essayé a existé.
        if (user is null
            || user.PasswordResetExpiresAt is null
            || user.PasswordResetExpiresAt < DateTime.UtcNow)
        {
            return new ResetPasswordWithTokenResult(false,
                "Ce lien n'est plus valable. Demandez-en un nouveau depuis l'écran de connexion.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(password);

        // Usage unique : le jeton est consommé, pas simplement périmé.
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;

        // Changer la serrure sans reprendre les doubles ne servirait à rien.
        var sessions = await _context.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var s in sessions) s.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Mot de passe réinitialisé pour l'utilisateur {UserId}; {Count} session(s) close(s).",
            user.Id, sessions.Count);

        return new ResetPasswordWithTokenResult(true,
            "Votre mot de passe a été changé. Vous pouvez maintenant vous connecter.");
    }
}
