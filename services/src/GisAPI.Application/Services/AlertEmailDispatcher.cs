using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Services;

/// <summary>
/// Reads the <c>alert_emails</c> table for a given company/alert type and fans out
/// <see cref="IEmailService.SendNotificationEmailAsync"/> calls to every recipient.
/// Falls back to the company admin's address if nothing is configured so alerts
/// never silently disappear.
/// </summary>
public class AlertEmailDispatcher : IAlertEmailDispatcher
{
    private readonly IGisDbContext _context;
    private readonly IEmailService _emailService;
    private readonly ILogger<AlertEmailDispatcher> _logger;

    public AlertEmailDispatcher(
        IGisDbContext context,
        IEmailService emailService,
        ILogger<AlertEmailDispatcher> logger)
    {
        _context = context;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<int> DispatchAsync(
        int companyId,
        string alertType,
        string title,
        string message,
        string? actionUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(alertType))
        {
            _logger.LogWarning("AlertEmailDispatcher.DispatchAsync called with empty alertType");
            return 0;
        }

        // Look up configured recipients for this company + alertType.
        // IgnoreQueryFilters because we may be called from a background scope with
        // no tenant context (PredictiveAlertService) and we must still enforce
        // CompanyId scoping explicitly.
        var recipients = await _context.AlertEmails
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.AlertType == alertType)
            .Select(a => a.Email)
            .ToListAsync(ct);

        // Fallback: if no recipients configured for this alert type, send to
        // every company admin user so alerts are never silently lost.
        if (recipients.Count == 0)
        {
            var adminEmails = await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.CompanyId == companyId
                         && u.Status == "active"
                         && u.Role != null
                         && u.Role.IsCompanyAdmin
                         && !string.IsNullOrEmpty(u.Email))
                .Select(u => u.Email)
                .ToListAsync(ct);

            if (adminEmails.Count == 0)
            {
                _logger.LogWarning(
                    "AlertEmailDispatcher: no recipients for company {CompanyId} alertType {AlertType} (no alert_emails row and no admin fallback)",
                    companyId, alertType);
                return 0;
            }

            recipients = adminEmails;
            _logger.LogInformation(
                "AlertEmailDispatcher: no alert_emails row for company {CompanyId} alertType {AlertType} — falling back to {Count} admin(s)",
                companyId, alertType, adminEmails.Count);
        }

        // De-duplicate (same address listed multiple times should only receive once).
        var unique = recipients
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sent = 0;
        foreach (var recipient in unique)
        {
            try
            {
                await _emailService.SendNotificationEmailAsync(
                    recipient,
                    recipient, // friendly name — use the email itself; we don't store a display name
                    alertType,
                    title,
                    message,
                    actionUrl,
                    ct);
                sent++;
            }
            catch (Exception ex)
            {
                // Isolate failures: one bad address must not block the rest.
                _logger.LogWarning(ex,
                    "AlertEmailDispatcher: failed to send alert email to {Recipient} (company {CompanyId}, type {AlertType})",
                    recipient, companyId, alertType);
            }
        }

        _logger.LogInformation(
            "AlertEmailDispatcher: dispatched {Sent}/{Total} alert emails for company {CompanyId} type {AlertType}",
            sent, unique.Count, companyId, alertType);

        return unique.Count;
    }

    public async Task SendTestAsync(int alertEmailId, CancellationToken ct = default)
    {
        var entry = await _context.AlertEmails
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == alertEmailId, ct);

        if (entry == null)
        {
            throw new InvalidOperationException($"AlertEmail {alertEmailId} introuvable");
        }

        await _emailService.SendNotificationEmailAsync(
            entry.Email,
            entry.Email,
            entry.AlertType,
            "Test de notification GIS Fleet",
            $"Ceci est un email de test pour confirmer que les alertes de type '{entry.AlertType}' sont bien reçues à cette adresse. Si vous recevez ce message, la configuration est correcte.",
            null,
            ct);

        _logger.LogInformation(
            "AlertEmailDispatcher: sent test email to {Recipient} (alert_email id={Id}, type={AlertType})",
            entry.Email, entry.Id, entry.AlertType);
    }
}
