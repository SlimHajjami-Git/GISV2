using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Raised by <c>DocumentExpiryMonitoringService</c> as soon as a vehicle
/// document (assurance / vignette / visite technique) enters its
/// reminder window — i.e. <c>today &gt;= expiry - reminderDays</c>.
///
/// <para>Calypso 6 (P8.1) — "Dès la date de notification, on affiche une
/// notification dans la cloche". Before this, the only path to learn an
/// échéance was approaching was the dedicated <c>/documents</c> page; the
/// bell only fired for runtime alerts (vitesse, géofencing, accident).</para>
///
/// <para>One event per (vehicle, documentType, expiryDate) — the watcher
/// dedupes against existing <c>document_expiry</c> notifications so the
/// bell does not refire every hour for the same approaching deadline.</para>
/// </summary>
public record DocumentExpiryNotificationEvent(
    int CompanyId,
    int VehicleId,
    string VehicleLabel,
    string DocumentType,           // "insurance" | "tax" | "technical_inspection"
    string DocumentTypeLabel,      // "Assurance" | "Vignette" | "Visite technique"
    DateTime ExpiryDate,
    int DaysRemaining              // negative if already expired
) : INotification;
