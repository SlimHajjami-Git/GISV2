namespace GisAPI.Application.Common.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// Send an email notification to a user.
    /// </summary>
    Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Send a notification email using a standard template.
    /// </summary>
    Task SendNotificationEmailAsync(string toEmail, string toName, string notificationType, string title, string message, string? actionUrl = null, CancellationToken ct = default);
}
