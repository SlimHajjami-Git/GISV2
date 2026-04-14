namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Dispatches alert emails to all recipients configured in the <c>alert_emails</c>
/// table for a given company + alert type. If no recipients are configured, falls
/// back to the company admin's email so nothing is lost.
/// </summary>
public interface IAlertEmailDispatcher
{
    /// <summary>
    /// Look up <c>alert_emails</c> for <paramref name="companyId"/> + <paramref name="alertType"/>
    /// and send a notification email to every recipient. Individual send failures are
    /// logged but do not throw — one bad address never blocks the rest.
    /// </summary>
    /// <param name="companyId">Target tenant.</param>
    /// <param name="alertType">One of: <c>"assurance"</c>, <c>"taxe_circulation"</c>, <c>"visite_technique"</c>, <c>"entretien"</c>.</param>
    /// <param name="title">Email subject/title (also shown in the template header).</param>
    /// <param name="message">Email body message.</param>
    /// <param name="actionUrl">Optional click-through URL appended to the template.</param>
    /// <returns>Number of recipients the dispatcher attempted to email (including the admin fallback).</returns>
    Task<int> DispatchAsync(
        int companyId,
        string alertType,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send a one-off test email to a single <c>alert_emails</c> recipient (by id).
    /// Used by the "Envoyer un test" button in the UI so admins can verify delivery.
    /// </summary>
    Task SendTestAsync(int alertEmailId, CancellationToken ct = default);
}
