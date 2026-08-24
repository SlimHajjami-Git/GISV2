using System.Security.Cryptography;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.ForgotPassword;

/// <summary>
/// Demande de réinitialisation de mot de passe.
/// </summary>
public record ForgotPasswordCommand(string Email) : IRequest<ForgotPasswordResult>;

/// <summary>
/// Réponse volontairement pauvre : elle ne dit jamais si l'adresse existe.
/// </summary>
public record ForgotPasswordResult(string Message);

/// <summary>
/// Émet un jeton de réinitialisation et envoie le lien par courriel.
///
/// <para><b>La réponse est TOUJOURS la même</b>, que l'adresse soit connue ou
/// non. Répondre « adresse inconnue » transformerait ce point d'entrée en
/// annuaire : n'importe qui pourrait vérifier, adresse par adresse, lesquelles
/// possèdent un compte. C'est pour cette même raison que le temps de réponse
/// ne doit pas trahir le cas — le courriel part sous budget de temps et, s'il
/// dépasse, se poursuit en arrière-plan.</para>
///
/// <para>Un compte non confirmé ou désactivé n'obtient pas de lien : il n'y a
/// rien à réinitialiser tant que l'adresse n'a pas été prouvée. Là encore, la
/// réponse reste identique.</para>
/// </summary>
public class ForgotPasswordCommandHandler
    : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    /// <summary>
    /// Formulation unique. Elle ne confirme rien : « si un compte existe ».
    /// </summary>
    private const string NeutralMessage =
        "Si un compte est associé à cette adresse, un lien de réinitialisation vient d'y être envoyé.";

    private readonly IGisDbContext _context;
    private readonly IEmailService _email;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IGisDbContext context,
        IEmailService email,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _context = context;
        _email = email;
        _logger = logger;
    }

    public async Task<ForgotPasswordResult> Handle(
        ForgotPasswordCommand request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0) return new ForgotPasswordResult(NeutralMessage);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        // Adresse inconnue, compte jamais confirmé ou désactivé : on s'arrête,
        // mais on rend exactement la même réponse.
        if (user is null || user.Status != "active")
        {
            _logger.LogInformation(
                "Réinitialisation demandée pour une adresse sans compte exploitable — réponse neutre rendue.");
            return new ForgotPasswordResult(NeutralMessage);
        }

        user.PasswordResetToken = GenerateToken();
        user.PasswordResetExpiresAt =
            DateTime.UtcNow.AddMinutes(AppRegistration.PasswordResetMinutes);
        await _context.SaveChangesAsync(ct);

        // Le jeton d'annulation de la requête n'est PAS propagé : un utilisateur
        // qui ferme son onglet ne doit pas annuler le courriel qui lui est destiné.
        await TrySendWithinAsync(user.Email, user.FirstName, user.PasswordResetToken!,
                                 TimeSpan.FromSeconds(8));

        return new ForgotPasswordResult(NeutralMessage);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>
    /// Envoie sans jamais retenir la réponse au-delà du budget. Le relais SMTP
    /// de ce déploiement met parfois plus de dix secondes à répondre ; laisser
    /// l'utilisateur devant une page bloquée serait pire que de lui rendre la
    /// main pendant que le courriel part.
    /// </summary>
    private async Task TrySendWithinAsync(
        string toEmail, string firstName, string token, TimeSpan budget)
    {
        var send = SendAsync(toEmail, firstName, token);
        var finished = await Task.WhenAny(send, Task.Delay(budget));
        if (finished != send)
        {
            _logger.LogWarning(
                "Courriel de réinitialisation non parti en moins de {Seconds}s — envoi poursuivi en arrière-plan.",
                budget.TotalSeconds);
        }
    }

    private async Task SendAsync(string toEmail, string firstName, string token)
    {
        try
        {
            var link = $"{AppUrls.PublicBaseUrl}/reinitialiser-mot-de-passe?token={token}";
            var minutes = AppRegistration.PasswordResetMinutes;
            var name = System.Net.WebUtility.HtmlEncode(firstName ?? string.Empty);

            var html = $@"
<div style=""font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:560px;margin:0 auto;color:#0f172a;"">
  <h2 style=""font-size:20px;margin:0 0 14px;"">Réinitialiser votre mot de passe</h2>
  <p style=""font-size:14.5px;line-height:1.6;color:#334155;margin:0 0 20px;"">
    Bonjour {name},<br/>
    vous avez demandé à changer le mot de passe de votre compte Calypso.
    Ce lien est valable {minutes} minutes et ne peut servir qu'une seule fois.
  </p>
  <p style=""margin:0 0 24px;"">
    <a href=""{link}"" style=""display:inline-block;padding:12px 28px;background:#4f46e5;color:#fff;text-decoration:none;border-radius:8px;font-weight:600;font-size:14px;"">Choisir un nouveau mot de passe</a>
  </p>
  <p style=""font-size:13px;line-height:1.6;color:#64748b;margin:0 0 8px;"">
    Si le bouton ne fonctionne pas, copiez cette adresse dans votre navigateur :<br/>
    <span style=""word-break:break-all;color:#4f46e5;"">{link}</span>
  </p>
  <p style=""font-size:13px;line-height:1.6;color:#64748b;margin:18px 0 0;border-top:1px solid #e2e8f0;padding-top:14px;"">
    <strong>Vous n'êtes pas à l'origine de cette demande ?</strong> Ignorez ce message :
    votre mot de passe actuel reste valable et n'a pas été modifié.
  </p>
</div>";

            await _email.SendEmailAsync(
                toEmail, firstName ?? string.Empty,
                "Réinitialiser votre mot de passe Calypso", html);
        }
        catch (Exception ex)
        {
            // Un échec d'envoi ne doit pas remonter : il révélerait à l'appelant
            // que l'adresse existe.
            _logger.LogError(ex, "Échec d'envoi du courriel de réinitialisation.");
        }
    }
}
