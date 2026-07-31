namespace GisAPI.Application.Common.Interfaces;

public interface IEmailService
{
    /// <summary>
    /// Send an email notification to a user.
    /// </summary>
    /// <param name="throwOnFailure">
    /// Par défaut l'implémentation JOURNALISE et AVALE ses erreurs : un incident de
    /// messagerie ne doit pas faire échouer l'action métier qui l'a déclenché
    /// (une alerte, un rapport). Mais l'appelant se retrouve alors incapable de
    /// savoir si l'envoi a abouti — au point de pouvoir annoncer « email envoyé »
    /// alors que le relais est injoignable. Les rares appelants pour qui l'issue
    /// compte, comme la confirmation d'inscription, passent <c>true</c> et gèrent
    /// l'exception eux-mêmes.
    /// </param>
    Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken ct = default, bool throwOnFailure = false);

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
