using System.Net;
using System.Net.Mail;
using GisAPI.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GisAPI.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            var smtpHost = _configuration["Emailer:SmtpHost"] ?? "mailing.topnet.tn";
            var smtpPort = int.Parse(_configuration["Emailer:SmtpPort"] ?? "25");
            var smtpUser = _configuration["Emailer:SmtpUser"] ?? "";
            var smtpPassword = _configuration["Emailer:SmtpPassword"] ?? "";
            var fromEmail = _configuration["Emailer:FromEmail"] ?? "contact@belive.tn";
            var fromName = _configuration["Emailer:FromName"] ?? "GPA Belive";
            var useSsl = (_configuration["Emailer:Ssl"] ?? "0") == "1";

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPassword),
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15000
            };

            var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(toEmail, toName));

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}: {Subject}", toEmail, subject);
        }
    }

    public async Task SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string htmlBody, byte[] attachmentBytes, string attachmentFileName, string attachmentMediaType, CancellationToken ct = default)
    {
        try
        {
            var smtpHost = _configuration["Emailer:SmtpHost"] ?? "mailing.topnet.tn";
            var smtpPort = int.Parse(_configuration["Emailer:SmtpPort"] ?? "25");
            var smtpUser = _configuration["Emailer:SmtpUser"] ?? "";
            var smtpPassword = _configuration["Emailer:SmtpPassword"] ?? "";
            var fromEmail = _configuration["Emailer:FromEmail"] ?? "contact@belive.tn";
            var fromName = _configuration["Emailer:FromName"] ?? "GPA Belive";
            var useSsl = (_configuration["Emailer:Ssl"] ?? "0") == "1";

            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials = new NetworkCredential(smtpUser, smtpPassword),
                EnableSsl = useSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(toEmail, toName));

            using var attachmentStream = new MemoryStream(attachmentBytes);
            message.Attachments.Add(new Attachment(attachmentStream, attachmentFileName, attachmentMediaType));

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email with attachment sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email with attachment to {Email}: {Subject}", toEmail, subject);
            throw;
        }
    }

    public async Task SendNotificationEmailAsync(string toEmail, string toName, string notificationType, string title, string message, string? actionUrl = null, CancellationToken ct = default)
    {
        var typeColor = notificationType switch
        {
            "geofence_event" or "geofence" => "#3b82f6",
            "speed_alert" => "#f59e0b",
            "driving_behavior" => "#dc2626",
            "maintenance_due" => "#7c3aed",
            "admin_action" => "#64748b",
            _ => "#3b82f6"
        };

        var typeLabel = notificationType switch
        {
            "geofence_event" or "geofence" => "Géofence",
            "speed_alert" => "Alerte Vitesse",
            "driving_behavior" => "Conduite",
            "maintenance_due" => "Maintenance",
            "admin_action" => "Action",
            _ => "Notification"
        };

        var actionButton = !string.IsNullOrEmpty(actionUrl)
            ? $@"<tr><td style=""padding:24px 30px 0;"">
                    <a href=""https://gpa.belive.tn{actionUrl}"" style=""display:inline-block;padding:12px 28px;background:{typeColor};color:#fff;text-decoration:none;border-radius:6px;font-weight:600;font-size:14px;"">Voir les détails</a>
                  </td></tr>"
            : "";

        var htmlBody = $@"
<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""></head>
<body style=""margin:0;padding:0;background:#f1f5f9;font-family:Inter,system-ui,-apple-system,sans-serif;"">
  <table width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f1f5f9;padding:40px 0;"">
    <tr><td align=""center"">
      <table width=""560"" cellpadding=""0"" cellspacing=""0"" style=""background:#fff;border-radius:12px;box-shadow:0 2px 8px rgba(0,0,0,0.06);overflow:hidden;"">
        <!-- Header -->
        <tr><td style=""background:linear-gradient(135deg,#1e3a5f 0%,#2d5a87 100%);padding:24px 30px;"">
          <table width=""100%""><tr>
            <td style=""color:#fff;font-size:20px;font-weight:700;"">GPA Belive</td>
            <td align=""right""><span style=""background:rgba(255,255,255,0.2);color:#fff;padding:4px 12px;border-radius:12px;font-size:11px;font-weight:500;"">{typeLabel}</span></td>
          </tr></table>
        </td></tr>
        <!-- Color bar -->
        <tr><td style=""height:4px;background:{typeColor};""></td></tr>
        <!-- Title -->
        <tr><td style=""padding:28px 30px 8px;"">
          <h2 style=""margin:0;font-size:18px;color:#1e293b;font-weight:600;"">{title}</h2>
        </td></tr>
        <!-- Message -->
        <tr><td style=""padding:8px 30px 20px;"">
          <p style=""margin:0;font-size:14px;color:#475569;line-height:1.6;"">{message}</p>
        </td></tr>
        <!-- Action Button -->
        {actionButton}
        <!-- Footer -->
        <tr><td style=""padding:28px 30px;border-top:1px solid #e2e8f0;margin-top:16px;"">
          <p style=""margin:0;font-size:11px;color:#94a3b8;line-height:1.5;"">
            Cet email a été envoyé automatiquement par la plateforme GPA Belive.<br>
            &copy; {DateTime.UtcNow.Year} Belive Technologies — Système de gestion de flotte
          </p>
        </td></tr>
      </table>
    </td></tr>
  </table>
</body>
</html>";

        await SendEmailAsync(toEmail, toName, $"[GPA] {title}", htmlBody, ct);
    }
}
