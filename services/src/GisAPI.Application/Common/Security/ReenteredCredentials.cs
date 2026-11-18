using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Ressaisie de l'e-mail et du mot de passe de la session ouverte, exigée avant de générer un jeton
/// d'API (migration 064) ou de créer une clé d'automatisation (migration 069). Une seule règle pour
/// les deux : l'e-mail ressaisi doit être CELUI de la session (pas celui d'un autre compte dont on
/// connaîtrait le mot de passe), et les deux refus disent et coûtent la même chose.
/// </summary>
public static class ReenteredCredentials
{
    /// <summary>Même message pour un e-mail qui n'est pas le sien et pour un mauvais mot de passe.</summary>
    public const string WrongCredentialsMessage = "E-mail ou mot de passe incorrect.";

    public const string MissingMessage = "Saisissez votre e-mail et votre mot de passe.";

    /// <summary>
    /// L'utilisateur de la session (rôle chargé) si l'e-mail et le mot de passe ressaisis sont les
    /// siens ; sinon <see cref="DomainException"/>. <paramref name="action"/> ne sert qu'au journal.
    /// </summary>
    public static async Task<User> VerifyAsync(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        ILogger logger,
        int userId,
        int companyId,
        string? email,
        string? password,
        string? ipAddress,
        string action,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new DomainException(MissingMessage);

        // Filtre société explicite : le filtre global est contourné pour un administrateur système.
        var user = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        // Le mot de passe est vérifié dans tous les cas, pour que les deux refus coûtent pareil.
        var emailMatches = string.Equals(email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase);
        var passwordMatches = passwordHasher.VerifyPassword(password, user.PasswordHash);
        if (!emailMatches || !passwordMatches)
        {
            // Pas d'adresse dans le journal : le champ reçoit parfois un mot de passe tapé au mauvais endroit.
            logger.LogWarning("{Action} refusée pour l'utilisateur {UserId} depuis {Ip} : identifiants incorrects",
                action, userId, ipAddress ?? "adresse inconnue");
            throw new DomainException(WrongCredentialsMessage);
        }

        return user;
    }
}
