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

        // Recipients come from two sources, unioned:
        //  1. alert_emails — external addresses (managers without an app account)
        //     the admin configured for this alert type;
        //  2. per-user opt-in — company users who ticked this alert type in their
        //     profile (assurance / taxe / visite technique / entretien map to
        //     User.Alert* flags) receive it at their own address.
        // IgnoreQueryFilters: we run in a background scope with no tenant context,
        // so CompanyId is enforced explicitly here.
        var external = await _context.AlertEmails
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.AlertType == alertType)
            .Select(a => a.Email)
            .ToListAsync(ct);

        var optedIn = await ResolveOptedInUsersAsync(companyId, alertType, ct);

        var recipients = external.Concat(optedIn).ToList();

        // Legacy fallback: a company that has configured NEITHER an external
        // address NOR any per-user opt-in for this type keeps the old
        // "all company admins" behaviour, so nobody silently stops receiving.
        // As soon as ONE recipient is explicitly configured for the type, that
        // explicit set becomes authoritative — this is exactly what lets one
        // colleague opt out of (e.g.) entretien while the other stays subscribed.
        if (recipients.Count == 0)
        {
            recipients = await _context.Users
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

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "AlertEmailDispatcher: no recipients for company {CompanyId} alertType {AlertType} (no alert_emails, no per-user opt-in, no admin fallback)",
                    companyId, alertType);
                return 0;
            }

            _logger.LogInformation(
                "AlertEmailDispatcher: company {CompanyId} type {AlertType} not configured per-user — falling back to {Count} admin(s)",
                companyId, alertType, recipients.Count);
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

    /// <summary>
    /// Company users who opted IN to this alert type via their profile
    /// (User.Alert* flags). Only the document/maintenance types map to a flag;
    /// anything else (e.g. "accident") has no per-user preference and returns an
    /// empty set, leaving the alert_emails + admin-fallback behaviour intact.
    /// </summary>
    private async Task<List<string>> ResolveOptedInUsersAsync(int companyId, string alertType, CancellationToken ct)
    {
        if (alertType is not ("assurance" or "taxe_circulation" or "visite_technique" or "entretien"))
            return new List<string>();

        // Small per-company set — pull the flags and pick the right one in memory
        // (avoids a non-translatable conditional inside the SQL predicate).
        var users = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.Status == "active" && !string.IsNullOrEmpty(u.Email))
            .Select(u => new
            {
                u.Email,
                u.AlertAssurance,
                u.AlertTaxeCirculation,
                u.AlertVisiteTechnique,
                u.AlertEntretien
            })
            .ToListAsync(ct);

        return users
            .Where(u => alertType switch
            {
                "assurance" => u.AlertAssurance,
                "taxe_circulation" => u.AlertTaxeCirculation,
                "visite_technique" => u.AlertVisiteTechnique,
                "entretien" => u.AlertEntretien,
                _ => false
            })
            .Select(u => u.Email!)
            .ToList();
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
