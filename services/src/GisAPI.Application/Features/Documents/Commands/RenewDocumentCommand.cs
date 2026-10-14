using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Commands;

public record RenewDocumentCommand(
    int VehicleId,
    string DocumentType,
    decimal Amount,
    DateTime PaymentDate,
    DateTime NewExpiryDate,
    string? DocumentNumber,
    string? Provider,
    string? Notes,
    string? DocumentUrl,
    // « Rappel avant (jours) » saisi dans la fenêtre : jamais envoyé ni enregistré jusqu'à
    // l'erreur n°4 de la campagne GPA (06/10/2026). Null = réglage inchangé.
    int? ReminderDays = null
) : ICommand<int>;



