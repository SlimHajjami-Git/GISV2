using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.ResendConfirmation;

/// <summary>
/// Renvoi du courriel de confirmation.
///
/// Indispensable : sans lui, un email perdu, expiré ou classé en indésirable laisse
/// le compte inutilisable pour toujours, et l'adresse bloquée par le contrôle
/// d'unicité — l'utilisateur ne peut même pas se réinscrire.
/// </summary>
public record ResendConfirmationCommand(string Email) : ICommand<ResendConfirmationResult>;

public record ResendConfirmationResult(string Message);

public class ResendConfirmationCommandHandler
    : IRequestHandler<ResendConfirmationCommand, ResendConfirmationResult>
{
    private readonly IGisDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<ResendConfirmationCommandHandler> _logger;

    public ResendConfirmationCommandHandler(
        IGisDbContext context,
        IEmailService emailService,
        ILogger<ResendConfirmationCommandHandler> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<ResendConfirmationResult> Handle(ResendConfirmationCommand request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        // Réponse VOLONTAIREMENT identique quel que soit le cas : adresse inconnue,
        // compte déjà actif, ou renvoi effectif. Distinguer les trois transformerait
        // cet endpoint public en moyen de savoir qui est inscrit chez nous.
        const string neutral =
            "Si un compte en attente de confirmation existe pour cette adresse, un nouvel email vient d'être envoyé.";

        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Email == email && u.Status == "pending", ct);

        if (user == null)
            return new ResendConfirmationResult(neutral);

        // Un nouveau jeton remplace l'ancien, qui cesse donc d'être valable.
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        user.EmailVerificationToken = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        user.EmailVerificationExpiresAt = DateTime.UtcNow.AddHours(AppRegistration.EmailConfirmationHours);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        try
        {
            var link = $"{AppUrls.PublicBaseUrl}/confirmation-email?token={user.EmailVerificationToken}";
            var html = $@"
<div style=""font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:560px;margin:0 auto;color:#0f172a;"">
  <h2 style=""font-size:20px;margin:0 0 14px;"">Confirmez votre adresse email</h2>
  <p style=""font-size:14.5px;line-height:1.6;color:#334155;margin:0 0 20px;"">
    Bonjour {System.Net.WebUtility.HtmlEncode(user.FirstName)}, voici un nouveau lien de confirmation.
  </p>
  <p style=""margin:0 0 24px;"">
    <a href=""{link}"" style=""display:inline-block;padding:12px 28px;background:#4f46e5;color:#fff;text-decoration:none;border-radius:8px;font-weight:600;font-size:14px;"">Confirmer mon adresse</a>
  </p>
  <p style=""font-size:12.5px;line-height:1.6;color:#64748b;margin:0;"">
    Ce lien est valable {AppRegistration.EmailConfirmationHours} heures et remplace le précédent.<br/>
    <span style=""word-break:break-all;color:#4f46e5;"">{link}</span>
  </p>
</div>";

            // Même précaution qu'à l'inscription : l'envoi est borné et ne retient
            // jamais la réponse. Le jeton de la requête n'est pas propagé — fermer
            // l'onglet ne doit pas annuler l'email demandé.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var send = _emailService.SendEmailAsync(
                user.Email, $"{user.FirstName} {user.LastName}".Trim(),
                "Confirmez votre adresse email", html, cts.Token);

            if (await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(8))) != send)
                _logger.LogWarning("Renvoi non parti en moins de 8s pour {Email} — poursuivi en arrière-plan.", email);
            else
                await send;
        }
        catch (Exception ex)
        {
            // Le jeton est déjà renouvelé : l'utilisateur peut réessayer.
            _logger.LogError(ex, "Renvoi du courriel de confirmation impossible pour {Email}", email);
        }

        return new ResendConfirmationResult(neutral);
    }
}
