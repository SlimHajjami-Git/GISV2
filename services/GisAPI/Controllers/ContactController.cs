using GisAPI.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Formulaire de contact du site public.
///
/// <para>Le document maître fait de ce formulaire le SEUL canal public : aucune
/// adresse, aucun téléphone ne doit apparaître sur le site. Le destinataire est
/// donc une configuration serveur (<c>Contact:Recipient</c>, env
/// <c>Contact__Recipient</c>) et n'est jamais exposé au navigateur.</para>
///
/// <para><b>Sans destinataire configuré, le point d'entrée répond 503 et ne
/// prétend rien.</b> C'est délibéré : un formulaire qui affiche « message
/// envoyé » sans destination fait perdre des demandes clients en silence, et
/// personne ne s'en aperçoit avant des semaines. Mieux vaut un refus visible.</para>
/// </summary>
[ApiController]
[Route("api/contact")]
public class ContactController : ControllerBase
{
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<ContactController> _logger;

    public ContactController(
        IEmailService email, IConfiguration config, ILogger<ContactController> logger)
    {
        _email = email;
        _config = config;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ContactRequest request, CancellationToken ct)
    {
        // La VALIDATION passe en premier : une requête malformée doit être
        // refusée qu'un destinataire soit configuré ou non. Dans l'autre ordre,
        // la configuration manquante masquait le défaut de saisie — et rendait
        // la validation intestable tant que le déploiement n'était pas réglé.
        var nom = (request.Nom ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var tel = (request.Telephone ?? string.Empty).Trim();
        var sujet = (request.Sujet ?? string.Empty).Trim();
        var message = (request.Message ?? string.Empty).Trim();

        if (nom.Length == 0 || email.Length == 0 || tel.Length == 0 || sujet.Length == 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Nom, e-mail, téléphone et sujet sont obligatoires."
            });
        }

        var recipient = _config["Contact:Recipient"];
        if (string.IsNullOrWhiteSpace(recipient))
        {
            _logger.LogError(
                "Formulaire de contact soumis mais Contact:Recipient n'est pas configuré — message NON transmis.");
            return StatusCode(503, new
            {
                success = false,
                message = "Le formulaire de contact n'est pas encore raccordé. Réessayez plus tard."
            });
        }

        var esc = (string v) => System.Net.WebUtility.HtmlEncode(v);
        var html = $@"
<div style=""font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:620px;color:#0f172a;"">
  <h2 style=""font-size:18px;margin:0 0 16px;"">Nouvelle demande depuis le site</h2>
  <table style=""border-collapse:collapse;font-size:14.5px;"">
    <tr><td style=""padding:6px 14px 6px 0;color:#64748b;"">Nom</td><td><strong>{esc(nom)}</strong></td></tr>
    <tr><td style=""padding:6px 14px 6px 0;color:#64748b;"">E-mail</td><td><strong>{esc(email)}</strong></td></tr>
    <tr><td style=""padding:6px 14px 6px 0;color:#64748b;"">Téléphone</td><td><strong>{esc(tel)}</strong></td></tr>
    <tr><td style=""padding:6px 14px 6px 0;color:#64748b;"">Sujet</td><td><strong>{esc(sujet)}</strong></td></tr>
  </table>
  <p style=""margin:20px 0 6px;color:#64748b;font-size:13px;"">Message</p>
  <div style=""white-space:pre-wrap;background:#f8fafc;border:1px solid #e2e8f0;border-radius:10px;padding:16px;font-size:14.5px;line-height:1.6;"">{esc(message)}</div>
</div>";

        try
        {
            // throwOnFailure: un échec doit remonter. Le service de messagerie
            // absorbe ses erreurs par défaut — l'écran afficherait alors une
            // confirmation pour un message jamais parti.
            await _email.SendEmailAsync(
                recipient, "Calypso", $"[Contact] {sujet} — {nom}", html, ct, throwOnFailure: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec de transmission d'une demande de contact.");
            return StatusCode(502, new
            {
                success = false,
                message = "Votre message n'a pas pu être transmis. Réessayez dans un instant."
            });
        }

        return Ok(new { success = true, message = "Message envoyé." });
    }
}

public record ContactRequest(
    string? Nom, string? Email, string? Telephone, string? Sujet, string? Message);
