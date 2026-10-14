using GisAPI.Application.Features.Documents;
using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.Drivers;

/// <summary>Contrôle du « Rappel avant (jours) » du permis, mêmes bornes que les échéances de véhicule.</summary>
internal static class DriverReminder
{
    public static int? Valider(int? rappel)
    {
        if (rappel is null) return null;
        if (rappel < ExpiryCalendar.MinReminderDays || rappel > ExpiryCalendar.MaxReminderDays)
            throw new DomainException(
                $"Le rappel doit être compris entre {ExpiryCalendar.MinReminderDays} et {ExpiryCalendar.MaxReminderDays} jours.");
        return rappel;
    }
}
