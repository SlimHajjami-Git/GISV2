using MediatR;

namespace GisAPI.Application.Features.FuelEntries.Commands;

/// <summary>
/// Import en masse de pleins : chaque ligne est une création indépendante.
/// Une ligne nulle (élément vide dans le JSON) est refusée à son rang.
/// </summary>
public record BulkCreateFuelEntriesCommand(
    IReadOnlyList<CreateFuelEntryCommand?> Lignes
) : IRequest<BulkCreateFuelEntriesResult>;

/// <summary>
/// Bilan de l'import. <c>Results</c> suit l'ordre des lignes envoyées
/// (l'écran Carburant retrouve la ligne source par son rang).
/// </summary>
public record BulkCreateFuelEntriesResult(
    int Total,
    int Success,
    int Failed,
    List<BulkFuelEntryLineResult> Results
);

/// <param name="Row">Rang de la ligne dans l'envoi, à partir de 1.</param>
/// <param name="Id">Identifiant du plein créé, 0 si la ligne est refusée.</param>
/// <param name="Error">Motif du refus, en français ; null si la ligne est enregistrée.</param>
public record BulkFuelEntryLineResult(
    int Row,
    int Id,
    bool Success,
    string? Error
);
