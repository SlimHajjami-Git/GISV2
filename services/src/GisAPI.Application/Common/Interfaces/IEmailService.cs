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

    /// <summary>
    /// Send an email with a single binary attachment (e.g. a PDF/.xlsx report).
    /// Optionally copies a second address in CC.
    /// </summary>
    Task SendEmailWithAttachmentAsync(string toEmail, string toName, string subject, string htmlBody, byte[] attachmentBytes, string attachmentFileName, string attachmentMediaType, CancellationToken ct = default, string? ccEmail = null);
}
